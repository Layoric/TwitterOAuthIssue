using Funq;
using ServiceStack;
using ServiceStack.Text;
using twitterauthtest2.ServiceInterface;

[assembly: HostingStartup(typeof(twitterauthtest2.AppHost))]

namespace twitterauthtest2;

public class AppHost : AppHostBase, IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder
        .ConfigureServices(services => {
            // Configure ASP.NET Core IOC Dependencies
        });

    public AppHost() : base("twitterauthtest2", typeof(MyServices).Assembly) {}

    public override void Configure(Container container)
    {
        // Configure ServiceStack only IOC, Config & Plugins
        SetConfig(new HostConfig {
            // Needs to be null to set SameSite: Lax
            UseSameSiteCookies = null,
        });
    }
}
