using Hangfire;
using Microsoft.Owin;
using Owin;

[assembly: OwinStartup(typeof(MyWebApplication.Startup))]
namespace MyWebApplication
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            // Map Dashboard to the `http://<your-app>/hangfire` URL.
            app.UseHangfireDashboard();
        }
    }
}