using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Platform.AiNewsDelivery.Infrastructure.Persistence;

/// <summary>
/// Factory de design-time para o <see cref="NewsDbContext"/>.
/// Usada exclusivamente pelo CLI do EF Core (<c>dotnet ef migrations add</c>)
/// para criar uma instância do DbContext sem o host completo rodando.
/// Esta classe não é registrada no DI — é descoberta automaticamente pelo EF Core
/// por reflection durante o design-time.
/// </summary>
internal sealed class NewsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<NewsDbContext>
{
    public NewsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NewsDbContext>()
            .UseSqlite("Data Source=news-delivery-design.db")
            .Options;

        return new NewsDbContext(options);
    }
}
