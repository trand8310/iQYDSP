using MainClient.Common;
using MainClient.Logging;
using MainClient.UiTask;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System.Diagnostics;
using System.Xml.Linq;
using static System.Net.WebRequestMethods;



namespace MainClient
{
    static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {

            ApplicationConfiguration.Initialize();

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += (sender, e) =>
            {
                Log.Error(e.Exception, "Application ThreadException");
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Log.Fatal(e.ExceptionObject as Exception, "UnhandledException");
            };

            AppDomain.CurrentDomain.FirstChanceException += (sender, e) =>
            {
                //Log.Debug(e.Exception, "FirstChanceException");
            };

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                Log.Error(e.Exception, "TaskScheduler UnobservedTaskException");
                e.SetObserved();
            };


            var appSettings = new AppSettings();
            UserConfigService.Init(appSettings);
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile("appsettings.user.json", optional: true, reloadOnChange: true)
                .Build();
            configuration.GetSection("AppSettings").Bind(appSettings);

            var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);
            Log.Logger = new LoggerConfiguration()
              .Enrich.FromLogContext()
              .MinimumLevel.Information()
              .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
              .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
              .WriteTo.Logger(lc => lc.WriteTo.File(Path.Combine(logDir, "app-.log"),rollingInterval: RollingInterval.Day))
              .WriteTo.Sink<UiLogSink>()
              .CreateLogger();

            var builder = new HostBuilder()
                .ConfigureServices((context, services) =>
                {

                    services.AddSingleton(appSettings);
                    services.AddHttpClient();


                    services.AddSingleton<AdTrafficAggregator>();
                    services.AddSingleton<AdeHelper>();

                    services.AddSingleton<AdxHelper>();
                    services.AddSingleton<DevHelper>();
                    services.AddSingleton<UrlHelper>();
                    services.AddSingleton<IpHelper>();
                    services.AddSingleton<ProxyTester>();
                    services.AddTransient<MainForm>();

                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                })
                .UseSerilog();


            var host = builder.Build();
            Application.ApplicationExit += (sender, e) =>
            {
            };
            Application.Run(host.Services.GetRequiredService<MainForm>());





            //var builder = new HostBuilder()
            //    .ConfigureServices((context, services) =>
            //    {
            //        // 读取配置文件
            //        var configuration = new ConfigurationBuilder()
            //            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            //            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            //            .Build();
            //        services.ConfigureWritable<AppSettings>(configuration.GetSection("App"));
            //        services.AddHttpClient();
            //        services.AddSingleton(configuration);

            //        services.AddSingleton<ProxyTester>();
            //        services.AddSingleton<AdxHelper>();
            //        services.AddSingleton<DevHelper>();
            //        services.AddSingleton<UrlHelper>();
            //        services.AddSingleton<IpHelper>();
            //        services.AddTransient<MainForm>();

            //    }).ConfigureLogging(logBuilder =>
            //    {
            //        logBuilder.SetMinimumLevel(LogLevel.Trace);
            //        logBuilder.AddLog4Net("log4net.config");
            //    });
            //var host = builder.Build();
            //using (var serviceScope = host.Services.CreateScope())
            //{
            //    var services = serviceScope.ServiceProvider;
            //    try
            //    {
            //        ApplicationConfiguration.Initialize();
            //        Application.Run(services.GetRequiredService<MainForm>());
            //    }
            //    catch (Exception ex)
            //    {
            //        Debug.WriteLine(ex.Message);
            //    }
            //}
        }
    }
}
