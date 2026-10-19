using Microsoft.EntityFrameworkCore;

namespace OidcMock;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options);
