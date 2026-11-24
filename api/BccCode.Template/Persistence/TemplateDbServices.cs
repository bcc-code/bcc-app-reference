using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BccCode.Template.Persistence;

public static class TemplateDbServices
{
    /// <summary>
    /// <see cref="TemplateDb"/> on the host's <see cref="NpgsqlDataSource"/>, its tables in <paramref name="schema"/>
    /// (<see cref="PersistenceSchema"/>). A factory, which also registers the scoped context the endpoints receive. The factory is
    /// what the singletons use (session storage, the key ring): they must not hold a scoped context.
    /// The app never applies migrations; the schema step does
    /// </summary>
    public static IServiceCollection AddTemplateDb(this IServiceCollection services, string schema)
    {
        services.AddSingleton(new TemplateDbSchema(schema));
        services.AddDbContextFactory<TemplateDb>((serviceProvider, options) =>
            TemplateDb.Configure(options).UseNpgsql(serviceProvider.GetRequiredService<NpgsqlDataSource>()));
        return services;
    }
}
