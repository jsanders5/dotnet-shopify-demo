using InventorySync.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InventorySync.Api.Tests;

public class InMemoryApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shopify:WebhookSecret"] = "test-webhook-secret"
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null) services.Remove(descriptor);

            // AddDbContext registers DbContextOptions<AppDbContext> as scoped, and
            // its configuration lambda re-runs once per DI scope (i.e. once per
            // HTTP request). Generating the database name inside the lambda would
            // give every request its own empty in-memory database; capturing the
            // name in a variable outside the lambda keeps it stable for the
            // lifetime of this factory so requests share state.
            var dbName = $"TestDb-{Guid.NewGuid()}";
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(dbName));
        });
    }
}
