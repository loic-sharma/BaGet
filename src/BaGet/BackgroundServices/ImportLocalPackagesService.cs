using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using BaGet.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Timer = System.Timers.Timer;

namespace BaGet.BackgroundServices
{
    public class ImportLocalPackagesService : BackgroundService
    {
        private readonly string _localPackages;
        private readonly IServiceProvider _serviceProvider;
        private readonly Timer _timer;
        private IPackageIndexingService _packageIndexingService;

        public ImportLocalPackagesService(IServiceProvider serviceProvider, IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            var options = configuration.Get<BaGetOptions>();
            _localPackages = options.LocalPackages;
            if (!string.IsNullOrWhiteSpace(_localPackages))
            {
                _localPackages = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _localPackages);

                if (!Directory.Exists(_localPackages)) Directory.CreateDirectory(_localPackages);
            }

            _timer = new Timer(300);
            _timer.Elapsed += Timer_Elapsed;
        }

        private IPackageIndexingService PackageIndexingService
        {
            get =>
                _packageIndexingService ??= _serviceProvider.CreateScope()
                    .ServiceProvider
                    .GetRequiredService<IPackageIndexingService>();
            set => _packageIndexingService = value;
        }

        private async void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            _timer.Stop();
            await ImportPackages(default);
        }

        private const string ExtName = ".nupkg";

        private async Task ImportPackages(CancellationToken stoppingToken)
        {
            var files = Directory.GetFiles(_localPackages);
            foreach (var file in files)
            {
                if (!ExtName.Equals(Path.GetExtension(file)))
                {
                    continue;
                }
                using (var stream = File.OpenRead(file))
                {
                    await PackageIndexingService.IndexAsync(stream, stoppingToken);
                }

                File.Delete(file);
            }

            PackageIndexingService = null;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!string.IsNullOrWhiteSpace(_localPackages))
            {
                await ImportPackages(stoppingToken);
                var watcher = new FileSystemWatcher(_localPackages);
                watcher.Created += WatcherCreated;
                watcher.EnableRaisingEvents = true;
                while (stoppingToken.IsCancellationRequested) await Task.Delay(100, stoppingToken);
            }
        }

        private void WatcherCreated(object sender, FileSystemEventArgs e)
        {
            _timer.Stop();
            _timer.Start();
        }
    }
}
