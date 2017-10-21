﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using RestSharp;
using Newtonsoft.Json;
using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace MPA_Bot.Modules.PSO2
{
    public class EmergencyQuestService
    {
        private Config config;
        private DiscordSocketClient client;

        public async Task Install(IServiceProvider _services)
        {
            client = _services.GetService<DiscordSocketClient>();
            config = _services.GetService<Config>();

            Task.Run(async () =>
            {
                while (true)
                {
                    Console.WriteLine("Waiting for next attempt");
                    await Task.Delay(1000 * 60 * 3);

                    try
                    {
                        if (config.ServerSettings.Count == 0)
                        {
                            Console.WriteLine("List is empty, skipping");
                            continue;
                        }

                        Dictionary<ulong, EmergencyQuestConfig> check = new Dictionary<ulong, EmergencyQuestConfig>(config.ServerSettings);
                        List<ulong> remove = new List<ulong>();

                        foreach (var kv in check)
                        {
                            if (kv.Value.ChannelSettings.Values.All(x => x.Count() == 0))
                            {
                                remove.Add(kv.Key);
                                Console.WriteLine($"Removing channel {kv.Key}");
                            }

                            
                        }

                        if (remove.Count() > 0)
                        {
                            Console.WriteLine("Writing changes");

                            foreach (var r in remove)
                                check.Remove(r);

                            config.ServerSettings = check;
                            config.Save();
                        }

                        Console.WriteLine("Starting automated download");
                        var request = (HttpWebRequest)WebRequest.Create("http://pso2.kaze.rip/eq/");
                        request.Method = "GET";
                        request.AllowReadStreamBuffering = false;

                        using (var response = await request.GetResponseAsync())
                        {
                            using (var responseStream = response.GetResponseStream())
                            {
                                using (var reader = new StreamReader(responseStream))
                                {
                                    var data = JsonConvert.DeserializeObject<List<EqList>>(await reader.ReadToEndAsync());

                                    Console.WriteLine("Data deserialized");

                                    for (int i = 0; i < data.Count(); i++)
                                    {
                                        if (data[i].Quests.All(x => x.Ship == 0))
                                            Console.WriteLine($"All ships are 0 in event {i}");
                                    }

                                    if (data.Count() == 0)
                                        Console.WriteLine("Data is empty");

                                    Broadcast(data);
                                }

                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{ex.Message}\n{ex.StackTrace ?? "No stack trace"}");
                    }
                }
            });
        }

        public async Task Broadcast(List<EqList> data, bool force = false)
        {
            try
            {
                Dictionary<ulong, EmergencyQuestConfig> check = new Dictionary<ulong, EmergencyQuestConfig>(config.ServerSettings);

                if (!File.Exists("quests.json"))
                {
                    Console.WriteLine("Creating cache");
                    JsonStorage.SerializeObjectToFile(data, "quests.json");
                    Console.WriteLine("Created cache; returning...");
                    return;
                }

                Console.WriteLine("Reading cache...");
                var cache = JsonStorage.DeserializeObjectFromFile<List<EqList>>("quests.json");

                if (data.First().Time != cache.First().Time || force)
                {
                    if (!force)
                        Console.WriteLine("New data!");

                    foreach (var server in check)
                    {
                        Console.WriteLine($"Server setting loop {server.Key}");

                        foreach (var setting in server.Value.ChannelSettings)
                        {
                            Console.WriteLine($"Channel setting loop {setting.Key}");

                            var channel = (ISocketMessageChannel)client.GetChannel(setting.Key);
                            if (channel == null)
                            {
                                Console.WriteLine("null channel");
                                continue; // TODO: MARK CHANNEL FOR REMOVAL
                            }

                            var eqs = data.First().Quests.Where(x => setting.Value.Contains(x.Ship)).ToList();

                            if (eqs.Count() == 0)
                            {
                                Console.WriteLine("No matched ships");
                                continue;
                            }

                            StringBuilder output = new StringBuilder();

                            output.AppendLine($"**Upcoming EQ in {(data.First().StartTime - DateTimeOffset.Now).Minutes} minutes!** ({data.First().StartTime.ToLocalTime().ToString("t")} EST)");

                            if (data.First().Quests.Count() == 10 && data.First().Quests.All(x => x.Name == data.First().Quests.First().Name))
                            {
                                output.AppendLine($"`ALL SHIPS:` {data.First().Quests.First().Name} ({data.First().Quests.First().JpName})");
                            }
                            else
                            {
                                foreach (var shipQuest in eqs)
                                    output.AppendLine($"`Ship {shipQuest.Ship.ToString("00")}:` {shipQuest.Name} ({shipQuest.JpName})");
                            }

                            Console.WriteLine(output.ToString());

                            await channel.SendMessageAsync(output.ToString());
                        }
                    }

                    if (data.First().Time != cache.First().Time)
                    {
                        Console.WriteLine("Updating cache...");
                        JsonStorage.SerializeObjectToFile(data, "quests.json");
                        Console.WriteLine("Cache updated");
                    }
                }
                else
                {
                    if (!force)
                        Console.WriteLine("Time is matched with cache, skipping");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}\n{ex.StackTrace ?? "No stack trace"}");
            }

            Console.WriteLine("Returnning to previous context");
        }
    }

    public class Eq
    {
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("jpName")]
        public string JpName { get; set; }
        [JsonProperty("ship")]
        public int Ship { get; set; }
    }

    public class EqList
    {
        [JsonProperty("time")]
        public DateTime UnmanagedTime { get; private set; }
        [JsonProperty("when")]
        public DateTime UnmanagedStartTime { get; private set; }

        [JsonProperty("eqs")]
        public List<Eq> Quests {
            get;
            set;
        }

        [JsonIgnore]
        public DateTimeOffset Time { get { return new DateTimeOffset(UnmanagedTime.Ticks, new TimeSpan(9, 0, 0)); } }

        [JsonIgnore]
        public DateTimeOffset StartTime { get { return new DateTimeOffset(UnmanagedStartTime.Ticks, new TimeSpan(9, 0, 0)); } }
    }
}