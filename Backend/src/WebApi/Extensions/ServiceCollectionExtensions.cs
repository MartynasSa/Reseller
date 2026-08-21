using Application.Models.Craigslist;
using Application.Models.Ebay;
using Application.Models.Polling;
using Application.Models.Telegram;
using Application.Ports;
using Application.Services;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IListingFilterService, ListingFilterService>();
        return services;
    }

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<DatabaseContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("PostgresConnection")));
        services.Configure<EbayOptions>(configuration.GetSection("Ebay"));
        services.AddHttpClient<EbaySourceService>();
        services.AddScoped<IListingSource, EbaySourceService>();

        services.Configure<CraigslistOptions>(configuration.GetSection("Craigslist"));
        services.AddHttpClient<CraigslistSourceService>();
        services.AddScoped<IListingSource, CraigslistSourceService>();

        services.Configure<TelegramOptions>(configuration.GetSection("Telegram"));
        services.AddHttpClient<TelegramNotificationSender>();
        services.AddScoped<INotificationSender, TelegramNotificationSender>();

        services.Configure<PollingOptions>(configuration.GetSection("Polling"));
        services.AddHostedService<WatchPollingWorker>();
        return services;
    }
}
