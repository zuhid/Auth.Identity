using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Zuhid.Auth.Base;
using Zuhid.Auth.Entities;

namespace Zuhid.Auth;

public class AuthContext(DbContextOptions<AuthContext> options) : IdentityDbContext<User, Role, Guid,
    UserClaim, UserRole, UserLogin, RoleClaim, UserToken>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        const string schema = "identity";
        builder.ToSnakeCase();
        builder.Entity<User>(entity => entity.ToTable("users", schema));
        builder.Entity<Role>(entity => entity.ToTable("role", schema));
        builder.Entity<UserRole>(entity => entity.ToTable("user_role", schema));
        builder.Entity<UserClaim>(entity => entity.ToTable("user_claim", schema));
        builder.Entity<UserLogin>(entity => entity.ToTable("user_login", schema));
        builder.Entity<RoleClaim>(entity => entity.ToTable("role_claim", schema));
        builder.Entity<UserToken>(entity => entity.ToTable("user_token", schema));
        builder.LoadCsvData();
    }
}

