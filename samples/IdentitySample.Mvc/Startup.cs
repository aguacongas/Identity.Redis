using IdentitySample.Models;
using IdentitySample.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IdentitySample
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddUserSecrets<Startup>()
                .AddEnvironmentVariables();

            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; set; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {

            // Add framework services.
            services.AddIdentity<ApplicationUser, IdentityRole>()
                .AddRedisStores(Configuration.GetValue<string>("RedisOptions:HostAndPort"))
                .AddDefaultTokenProviders();

            var twitterConsumerKey = Configuration["Authentication:Twitter:ConsumerKey"];
            if (!string.IsNullOrEmpty(twitterConsumerKey))
            {
                services.AddAuthentication().AddTwitter(twitterOptions =>
                {
                    twitterOptions.ConsumerKey = twitterConsumerKey;
                    twitterOptions.ConsumerSecret = Configuration["Authentication:Twitter:ConsumerSecret"];
                });
            }

            services.AddMvc();

            // Add application services.
            services.AddTransient<IEmailSender, AuthMessageSender>()
                .AddTransient<ISmsSender, AuthMessageSender>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseStaticFiles();

            app.UseAuthentication();

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}