using Microsoft.EntityFrameworkCore;

namespace Infrastructure;

public class DatabaseContext(DbContextOptions<DatabaseContext> options) : DbContext(options)
{
}
