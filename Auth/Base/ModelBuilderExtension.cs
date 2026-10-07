using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Zuhid.Auth.Base;

public static class ModelBuilderExtension
{
    public static ModelBuilder ToSnakeCase(this ModelBuilder builder)
    {
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            entity.SetSchema(entity.GetSchema() ?? entity.ClrType.Namespace!.Split('.').Last().ToSnakeCase()); // Default schema to the entity's parent folder (namespace leaf)
            entity.SetTableName(entity.GetTableName()!.ToSnakeCase()); // Convert table name to snake_case
            entity.GetProperties().ToList().ForEach(property => property.SetColumnName(property.Name!.ToSnakeCase())); // Convert column names to snake_case
            entity.GetKeys().ToList().ForEach(key => key.SetName(key.GetName()?.ToLower())); // Convert key names to lower case
            entity.GetForeignKeys().ToList().ForEach(fk => fk.SetConstraintName(fk.GetConstraintName()?.ToLower())); // Convert foreign key names to lower case
            entity.GetIndexes().ToList().ForEach(index => index.SetDatabaseName(index.GetDatabaseName()?.ToLower())); // Convert index names to lower case
        }
        return builder;
    }

    public static void LoadCsvData(this ModelBuilder builder)
    {
        if (!EF.IsDesignTime)
        {
            return;
        }
        var loadMethod = typeof(CsvSerializer).GetMethod(nameof(CsvSerializer.Load))!;
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            var namespacePart = clrType.Namespace?.Split('.').Last()!;
            var entityName = clrType.Name.Replace("Entity", "");
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dataload", namespacePart, $"{entityName}.csv");
            if (!File.Exists(filePath))
            {
                continue;
            }
            var data = (IEnumerable<object>)loadMethod.MakeGenericMethod(clrType).Invoke(null, [filePath])!;
            builder.Entity(clrType).HasData(data);
        }
    }

    private static string ToSnakeCase(this string str)
    {
        var result = Regex.Replace(str, "([A-Z][a-z]|(?<=[a-z])[^a-z]|(?<=[A-Z])[0-9_])", "_$1").ToLower();
        return result.StartsWith('_') ? result[1..] : result;
    }
}
