using Application.Models.Ebay;
using Application.Ports;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // No Application-layer services to register yet; kept for symmetry with
        // AddInfrastructureServices and to be ready for future additions.
        return services;
    }

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<DatabaseContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("PostgresConnection")));
        services.Configure<EbayOptions>(configuration.GetSection("Ebay"));
        services.AddHttpClient<EbaySourceService>();
        services.AddScoped<IListingSource, EbaySourceService>();
        return services;
    }
}
