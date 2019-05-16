﻿using MinerPlugin;
using MinerPluginToolkitV1;
using MinerPluginToolkitV1.ExtraLaunchParameters;
using Newtonsoft.Json;
using NiceHashMinerLegacy.Common;
using NiceHashMinerLegacy.Common.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CorruptedPlugin
{
    public class Corrupted : MinerBase
    {
        private string _devices;

        private string _extraLaunchParameters = "";

        private int _apiPort;

        private AlgorithmType _algorithmType;

        private readonly HttpClient _httpClient = new HttpClient();

        public Corrupted(string uuid) : base(uuid)
        { }

        protected virtual string AlgorithmName(AlgorithmType algorithmType)
        {
            switch (algorithmType)
            {
                case AlgorithmType.Lyra2Z: return "lyra2z";
                case AlgorithmType.Skunk: return "skunk";
                case AlgorithmType.X16R: return "x16r";
                default: return "";
            }
        }

        private double DevFee
        {
            get
            {
                return 1.0;
            }
        }

        public async override Task<ApiData> GetMinerStatsDataAsync()
        {
            var ad = new ApiData();
            try
            {
                var summaryApiResult = await _httpClient.GetStringAsync($"http://127.0.0.1:{_apiPort}/summary");
                var summary = JsonConvert.DeserializeObject<JsonApiResponse>(summaryApiResult);

                var gpuDevices = _miningPairs.Select(pair => pair.Device);
                var perDeviceSpeedInfo = new Dictionary<string, IReadOnlyList<AlgorithmTypeSpeedPair>>();
                var perDevicePowerInfo = new Dictionary<string, int>();
                var totalSpeed = summary.hashrate;
                var totalPowerUsage = 0.0;

                foreach (var gpuDevice in gpuDevices)
                {
                    var currentStats = summary.gpus.Where(devStats => devStats.gpu_id == gpuDevice.ID).FirstOrDefault();
                    if (currentStats == null) continue;
                    perDeviceSpeedInfo.Add(gpuDevice.UUID, new List<AlgorithmTypeSpeedPair>() { new AlgorithmTypeSpeedPair(_algorithmType, currentStats.hashrate * (1 - DevFee * 0.01)) });
                    totalPowerUsage += currentStats.power;
                    perDevicePowerInfo.Add(gpuDevice.UUID, currentStats.hashrate);
                }


                ad.AlgorithmSpeedsTotal = new List<AlgorithmTypeSpeedPair> { new AlgorithmTypeSpeedPair(_algorithmType, totalSpeed * (1 - DevFee * 0.01)) };
                ad.PowerUsageTotal = Convert.ToInt32(totalPowerUsage);
                ad.AlgorithmSpeedsPerDevice = perDeviceSpeedInfo;
                ad.PowerUsagePerDevice = perDevicePowerInfo;

            }
            catch {}

            return ad;
        }

        public override async Task<BenchmarkResult> StartBenchmark(CancellationToken stop, BenchmarkPerformanceType benchmarkType = BenchmarkPerformanceType.Standard)
        {
            var benchmarkTime = 20; // in seconds
            switch (benchmarkType)
            {
                case BenchmarkPerformanceType.Quick:
                    benchmarkTime = 20;
                    break;
                case BenchmarkPerformanceType.Standard:
                    benchmarkTime = 60;
                    break;
                case BenchmarkPerformanceType.Precise:
                    benchmarkTime = 120;
                    break;
            }

            var algo = AlgorithmName(_algorithmType);

            var commandLine = $"--algo {algo} --devices {_devices} --benchmark --time-limit {benchmarkTime} {_extraLaunchParameters}";
            var binPathBinCwdPair = GetBinAndCwdPaths();
            var binPath = binPathBinCwdPair.Item1;
            var binCwd = binPathBinCwdPair.Item2;
            var bp = new BenchmarkProcess(binPath, binCwd, commandLine, GetEnvironmentVariables());

            var benchHashes = 0d;
            var counter = 0;
            var benchHashResult = 0d;

            bp.CheckData = (string data) =>
            {
                var hashrateFoundPair = MinerToolkit.TryGetHashrateAfter(data, "Total");
                var hashrate = hashrateFoundPair.Item1;
                var found = hashrateFoundPair.Item2;

                if (data.Contains("Time limit is reached."))
                    return new BenchmarkResult { AlgorithmTypeSpeeds = new List<AlgorithmTypeSpeedPair> { new AlgorithmTypeSpeedPair(_algorithmType, benchHashResult) }, Success = true };

                if (!found) return new BenchmarkResult { AlgorithmTypeSpeeds = new List<AlgorithmTypeSpeedPair> { new AlgorithmTypeSpeedPair(_algorithmType, benchHashResult) }, Success = false };


                benchHashes += hashrate;
                counter++;

                benchHashResult = (benchHashes / counter) * (1 - DevFee * 0.01);
                if (_algorithmType == AlgorithmType.X16R)
                {
                    // Quick adjustment, x16r speeds are overestimated by around 3.5
                    benchHashResult /= 3.5;
                }

                return new BenchmarkResult { AlgorithmTypeSpeeds = new List<AlgorithmTypeSpeedPair> { new AlgorithmTypeSpeedPair(_algorithmType, benchHashResult) }, Success = false };
            };

            var benchmarkTimeout = TimeSpan.FromSeconds(benchmarkTime + 5);
            var benchmarkWait = TimeSpan.FromMilliseconds(500);
            var t = MinerToolkit.WaitBenchmarkResult(bp, benchmarkTimeout, benchmarkWait, stop);
            return await t;
        }


        public override Tuple<string, string> GetBinAndCwdPaths()
        {
            var pluginRoot = Path.Combine(Paths.MinerPluginsPath(), _uuid);
            var pluginRootBins = Path.Combine(pluginRoot, "bins");
            var binPath = Path.Combine(pluginRootBins, "t-rex.exe");
            var binCwd = pluginRootBins;
            return Tuple.Create(binPath, binCwd);
        }

        protected override void Init()
        {
            var singleType = MinerToolkit.GetAlgorithmSingleType(_miningPairs);
            _algorithmType = singleType.Item1;
            bool ok = singleType.Item2;
            if (!ok)
            {
                Logger.Info(_logGroup, "Initialization of miner failed. Algorithm not found!");
                throw new InvalidOperationException("Invalid mining initialization");
            }
            // all good continue on

            // init command line params parts
            var orderedMiningPairs = _miningPairs.ToList();
            orderedMiningPairs.Sort((a, b) => a.Device.ID.CompareTo(b.Device.ID));
            _devices = string.Join(",", orderedMiningPairs.Select(p => p.Device.ID));
            if (MinerOptionsPackage != null)
            {
                // TODO add ignore temperature checks
                var generalParams = Parser.Parse(orderedMiningPairs, MinerOptionsPackage.GeneralOptions);
                var temperatureParams = Parser.Parse(orderedMiningPairs, MinerOptionsPackage.TemperatureOptions);
                _extraLaunchParameters = $"{generalParams} {temperatureParams}".Trim();
            }
        }

        protected override string MiningCreateCommandLine()
        {
            // API port function might be blocking
            _apiPort = FreePortsCheckerManager.GetAvaliablePortFromSettings(); // use the default range
            // instant non blocking
            var url = "sdasd";
            var algo = AlgorithmName(_algorithmType);

            var commandLine = $"--algo {algo} --url {url} --user {_username} --api-bind-http 127.0.0.1:{_apiPort} --devices {_devices} {_extraLaunchParameters}";
            return commandLine;
        }
    }
}
