using Microsoft.Extensions.DependencyInjection;
using ServiceStack;
using ServiceStack.Auth;
using ServiceStack.Configuration;
using ServiceStack.FluentValidation;
using ServiceStack.Text;

[assembly: HostingStartup(typeof(twitterauthtest2.ConfigureAuth))]

namespace twitterauthtest2
{
    // Add any additional metadata properties you want to store in the Users Typed Session
    public class CustomUserSession : AuthUserSession
    {
    }
    
// Custom Validator to add custom validators to built-in /register Service requiring DisplayName and ConfirmPassword
    public class CustomRegistrationValidator : RegistrationValidator
    {
        public CustomRegistrationValidator()
        {
            RuleSet(ApplyTo.Post, () =>
            {
                RuleFor(x => x.DisplayName).NotEmpty();
                RuleFor(x => x.ConfirmPassword).NotEmpty();
            });
        }
    }

    public class ConfigureAuth : IHostingStartup
    {
        public void Configure(IWebHostBuilder builder) => builder
            .ConfigureServices(services => {
                //services.AddSingleton<ICacheClient>(new MemoryCacheClient()); //Store User Sessions in Memory Cache (default)
            })
            .ConfigureAppHost(appHost => {
                var appSettings = appHost.AppSettings;
                appHost.Plugins.Add(new AuthFeature(() => new CustomUserSession(),
                    new IAuthProvider[] {
                        new CredentialsAuthProvider(appSettings),     /* Sign In with Username / Password credentials */
                        // new TwitterAuthProvider(appSettings) //Infinite loop on authenticate
                        new TwitterUpdateOAuthProvider(appSettings) //works
                        {
                            ConsumerKey = "igSkRbWlDKtB9znBp9lDN9X6H",
                            ConsumerSecret = "qYQRR1pGPobSih1zY1I4Ky9xgC9lJWtt9WIrFVVCxoh2NpUssb",
                            RedirectUrl = "https://localhost:5001/",
                            CallbackUrl = "https://localhost:5001/auth/twitter"
                        }
                    }));

                appHost.Plugins.Add(new RegistrationFeature()); //Enable /register Service

                //override the default registration validation with your own custom implementation
                appHost.RegisterAs<CustomRegistrationValidator, IValidator<Register>>();
            });
    }

    public class TwitterUpdateOAuthProvider : TwitterAuthProvider
    {
        public TwitterUpdateOAuthProvider(IAppSettings appSettings) : base(appSettings)
        {
            this.AuthorizeUrl = appSettings.Get("oauth.twitter.AuthorizeUrl", DefaultAuthorizeUrl);
        }
        public override async Task<object> AuthenticateAsync(IServiceBase authService, IAuthSession session, Authenticate request,
            CancellationToken token = new CancellationToken())
        {
            var tokens = Init(authService, ref session, request);
            var ctx = CreateAuthContext(authService, session, tokens);

            //Transferring AccessToken/Secret from Mobile/Desktop App to Server
            if (request.AccessToken != null && request.AccessTokenSecret != null)
            {
                tokens.AccessToken = request.AccessToken;
                tokens.AccessTokenSecret = request.AccessTokenSecret;

                var validToken = await AuthHttpGateway.VerifyTwitterAccessTokenAsync(
                    ConsumerKey, ConsumerSecret,
                    tokens.AccessToken, tokens.AccessTokenSecret, token).ConfigAwait();

                if (validToken == null)
                    return HttpError.Unauthorized("AccessToken is invalid");

                if (!string.IsNullOrEmpty(request.UserName) && validToken.UserId != request.UserName)
                    return HttpError.Unauthorized("AccessToken does not match UserId: " + request.UserName);

                tokens.UserId = validToken.UserId;
                session.IsAuthenticated = true;

                var failedResult = await OnAuthenticatedAsync(authService, session, tokens, new Dictionary<string, string>(), token).ConfigAwait();
                var isHtml = authService.Request.IsHtml();
                if (failedResult != null)
                    return ConvertToClientError(failedResult, isHtml);

                return isHtml
                    ? await authService.Redirect(SuccessRedirectUrlFilter(ctx, RedirectUrl.SetParam("s", "1"))).SuccessAuthResultAsync(authService,session).ConfigAwait()
                    : null; //return default AuthenticateResponse
            }
            
            //Default OAuth logic based on Twitter's OAuth workflow
            if ((!tokens.RequestTokenSecret.IsNullOrEmpty() && !request.oauth_token.IsNullOrEmpty()) || 
                (!request.oauth_token.IsNullOrEmpty() && !request.oauth_verifier.IsNullOrEmpty()))
            {
                if (OAuthUtils.AcquireAccessToken(tokens.RequestTokenSecret, request.oauth_token, request.oauth_verifier))
                {
                    session.IsAuthenticated = true;
                    tokens.AccessToken = OAuthUtils.AccessToken;
                    tokens.AccessTokenSecret = OAuthUtils.AccessTokenSecret;

                    //Haz Access
                    return await OnAuthenticatedAsync(authService, session, tokens, OAuthUtils.AuthInfo, token).ConfigAwait()
                        ?? await authService.Redirect(SuccessRedirectUrlFilter(ctx, this.RedirectUrl.SetParam("s", "1"))).SuccessAuthResultAsync(authService,session).ConfigAwait();
                }

                //No Joy :(
                tokens.RequestToken = null;
                tokens.RequestTokenSecret = null;
                await this.SaveSessionAsync(authService, session, SessionExpiry, token).ConfigAwait();
                return authService.Redirect(FailedRedirectUrlFilter(ctx, RedirectUrl.SetParam("f", "AccessTokenFailed")));
            }
            if (OAuthUtils.AcquireRequestToken())
            {
                tokens.RequestToken = OAuthUtils.RequestToken;
                tokens.RequestTokenSecret = OAuthUtils.RequestTokenSecret;
                await this.SaveSessionAsync(authService, session, SessionExpiry, token).ConfigAwait();

                //Redirect to OAuth provider to approve access
                return authService.Redirect(AccessTokenUrlFilter(ctx, this.AuthorizeUrl
                    .AddQueryParam("oauth_token", tokens.RequestToken)
                    .AddQueryParam("oauth_callback", session.ReferrerUrl)
                    .AddQueryParam(Keywords.State, session.Id) // doesn't support state param atm, but it's here when it does
                ));
            }

            return authService.Redirect(FailedRedirectUrlFilter(ctx, session.ReferrerUrl.SetParam("f", "RequestTokenFailed")));
        }
    }
}
