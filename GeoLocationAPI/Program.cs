using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GeoLocationAPI
{
    /// <summary>
    /// Represents the current application.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// The main entry point to the application
        /// </summary>
        /// <param name="args">The arguments provided at start-up, if any.</param>
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        /// <summary>
        /// Builds a new web host for the application.
        /// </summary>
        /// <param name="args">The command-line arguments, if any.</param>
        /// <returns></returns>
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
