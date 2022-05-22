using ServiceStack;
using twitterauthtest2.ServiceModel;

namespace twitterauthtest2.ServiceInterface;

public class MyServices : Service
{
    public object Any(Hello request)
    {
        return new HelloResponse { Result = $"Hello, {request.Name}!" };
    }
}