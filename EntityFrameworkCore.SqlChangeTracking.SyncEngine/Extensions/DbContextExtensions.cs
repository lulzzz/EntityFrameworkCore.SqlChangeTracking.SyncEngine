﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.SqlChangeTracking.AsyncLinqExtensions;
using EntityFrameworkCore.SqlChangeTracking.Extensions;
using EntityFrameworkCore.SqlChangeTracking.Extensions.Internal;
using EntityFrameworkCore.SqlChangeTracking.Models;
using EntityFrameworkCore.SqlChangeTracking.Sql;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Models;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine.Extensions
{
    internal static class InternalDbContextExtensions
    {
        public static IAsyncEnumerable<IChangeTrackingEntry<T>> Next<T>(this DbContext db, IEntityType entityType, string syncContext) where T : class, new()
        {
            var sql = SyncEngineSqlStatements.GetNextChangeSetExpression(entityType, syncContext);

            return new AsyncEnumerableWrapper<T>(db.ToChangeSet<T>(sql), sql);
        }

        public static IAsyncEnumerable<IChangeTrackingEntry<T>> All<T>(this DbContext db, IEntityType entityType, string syncContext) where T : class, new()
        {
            var sql = SyncEngineSqlStatements.GetAllChangeSetsExpression(entityType, syncContext);

            return new AsyncEnumerableWrapper<T>(db.ToChangeSet<T>(sql), sql);
        }

        public static ValueTask<IChangeTrackingEntry<T>[]> NextHelper<T>(this DbContext db, IEntityType entityType, string syncContext) where T : class, new()
        {
            var sql = SyncEngineSqlStatements.GetNextChangeSetExpression(entityType, syncContext);

            return db.ToChangeSet<T>(sql).ToArrayAsync();
        }
    }

    public static class DbContextExtensions
    {
        public static IAsyncEnumerable<IChangeTrackingEntry<T>> Next<T>(this IChangesQueryContext<T> context, string syncContext = "Default") where T : class, new()
        {
            var dbContext = context.DbSet.GetService<ICurrentDbContext>().Context;

            var entityType = dbContext.Model.FindEntityType(typeof(T));

            return dbContext.Next<T>(entityType, syncContext);
        }

        public static IAsyncEnumerable<IChangeTrackingEntry<T>> All<T>(this IChangesQueryContext<T> context, string syncContext = "Default") where T : class, new()
        {
            var dbContext = context.DbSet.GetService<ICurrentDbContext>().Context;

            var entityType = dbContext.Model.FindEntityType(typeof(T));

            return dbContext.All<T>(entityType, syncContext);
        }

        //public static async ValueTask<long?> GetLastChangeVersionAsync(this DbContext db, IEntityType entityType, string syncContext)
        //{
        //    await using var innerContext = new ContextForQueryType<LastSyncedChangeVersion>(db.Database.GetDbConnection(), m => m.ApplyConfiguration(new LastSyncedChangeVersion()));

        //    var entry = await innerContext.Set<LastSyncedChangeVersion>().AsQueryable().FirstOrDefaultAsync(t => t.TableName == entityType.GetFullTableName() && t.SyncContext == syncContext);

        //    return entry?.LastSyncedVersion;
        //}

        //public static ValueTask<long?> GetLastChangeVersionAsync<T>(this DbContext db, string syncContext)
        //{
        //    var entityType = db.Model.FindEntityType(typeof(T));

        //    return db.GetLastChangeVersionAsync(entityType, syncContext);
        //}

        //public static async ValueTask<long?> GetNextVersionAsync<T>(this DbContext dbContext, string syncContext) where T : class
        //{
        //    var entityType = dbContext.Model.FindEntityType(typeof(T));

        //    var lastChangeVersion = SyncEngineSqlStatements.GetLastChangeVersionExpression(entityType, syncContext);

        //    var sql = ChangeTableSqlStatements.GetNextChangeVersionExpression(entityType, lastChangeVersion);

        //    return (await dbContext.SqlQueryAsync(() => new { NextVersion = 0L }, sql)).FirstOrDefault()?.NextVersion;
        //}

        //public static long? GetNextVersion<T>(this DbContext dbContext, string syncContext) where T : class
        //{
        //    var entityType = dbContext.Model.FindEntityType(typeof(T));

        //    var lastChangeVersion = SyncEngineSqlStatements.GetLastChangeVersionExpression(entityType, syncContext);

        //    var sql = ChangeTableSqlStatements.GetNextChangeVersionExpression(entityType, lastChangeVersion);

        //    return dbContext.SqlQuery(() => new { NextVersion = 0L }, sql).FirstOrDefault()?.NextVersion;
        //}

        public static ValueTask SetLastChangedVersionAsync(this DbContext db, IEntityType entityType, string syncContext, long version)
        {
            //await using var innerContext = new ContextForQueryType<LastSyncedChangeVersion>(db.Database.GetDbConnection(), m => m.ApplyConfiguration(new LastSyncedChangeVersion()));

            //innerContext.Set<LastSyncedChangeVersion>().Update(new LastSyncedChangeVersion()
            //{
            //    TableName = entityType.GetFullTableName(),
            //    SyncContext = syncContext,
            //    LastSyncedVersion = version
            //});

            ////await innerContext.Database.UseTransactionAsync(db.Database.CurrentTransaction.GetDbTransaction());

            //await innerContext.SaveChangesAsync(false);

            var tableName = nameof(LastSyncedChangeVersion);

            var keyColumn = nameof(LastSyncedChangeVersion.TableName);
            var versionColumn = nameof(LastSyncedChangeVersion.LastSyncedVersion);
            var key = entityType.GetFullTableName();

            var sqlString = $@"UPDATE {tableName} set {versionColumn}={version}
                               WHERE {keyColumn}='{key}' AND SyncContext='{syncContext}'
                               if @@rowcount = 0
                               begin
                                  INSERT INTO {tableName} ({keyColumn}, SyncContext, {versionColumn}) values ('{key}', '{syncContext}' ,{version})
                               end";

            return new ValueTask(db.Database.ExecuteSqlRawAsync(sqlString));
        }

        public static ValueTask InitializeSyncEngine(this DbContext dbContext, IEntityType entityType, string syncContext, bool markAllSynced = false)
        {
            var initialVersionString = markAllSynced ? "(SELECT CHANGE_TRACKING_CURRENT_VERSION())" : "0";

            var sql = $@"BEGIN
                       IF NOT EXISTS (SELECT * FROM {nameof(LastSyncedChangeVersion)} 
                                       WHERE TableName = '{entityType.GetFullTableName()}'
                                       AND SyncContext = '{syncContext}')
                       BEGIN
                           INSERT INTO {nameof(LastSyncedChangeVersion)} (TableName, SyncContext, LastSyncedVersion)
                           VALUES ('{entityType.GetFullTableName()}', '{syncContext}', {initialVersionString})
                       END
                    END";

            return new ValueTask(dbContext.Database.ExecuteSqlRawAsync(sql));
        }

        private class ContextForQueryType<T> : DbContext where T : class
        {
            private readonly DbConnection connection;
            private readonly Action<ModelBuilder> _modelBuilderConfig;

            public ContextForQueryType(DbConnection connection, Action<ModelBuilder> modelBuilderConfig = null)
            {
                this.connection = connection;
                _modelBuilderConfig = modelBuilderConfig;
            }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                optionsBuilder.UseSqlServer(connection);
                Database.AutoTransactionsEnabled = false;
                base.OnConfiguring(optionsBuilder);
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                _modelBuilderConfig?.Invoke(modelBuilder);

                if (_modelBuilderConfig == null)
                    modelBuilder.Entity<T>().HasNoKey();

                base.OnModelCreating(modelBuilder);
            }
        }
    }

    //internal static class InternalDbContextExtensions
    //{
    //    public static Task<List<T>> SqlQueryAsync<T>(this DbContext db, Func<T> targetType, string sql, params object[] parameters) where T : class
    //    {
    //        using var db2 = new ContextForQueryType<T>(db.Database.GetDbConnection());

    //        return db2.Set<T>().FromSqlRaw(sql, parameters).ToListAsync();
    //    }
    //    public static IList<T> SqlQuery<T>(this DbContext db, Func<T> targetType, string sql, params object[] parameters) where T : class
    //    {
    //        return SqlQuery<T>(db, sql, parameters);
    //    }
    //    public static IList<T> SqlQuery<T>(this DbContext db, string sql, params object[] parameters) where T : class
    //    {
    //        using var db2 = new ContextForQueryType<T>(db.Database.GetDbConnection());

    //        return db2.Set<T>().FromSqlRaw(sql, parameters).ToList();
    //    }

    //    class ContextForQueryType<T> : DbContext where T : class
    //    {
    //        DbConnection con;

    //        public ContextForQueryType(DbConnection con)
    //        {
    //            this.con = con;
    //        }
    //        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    //        {
    //            //switch on the connection type name to enable support multiple providers
    //            //var name = con.GetType().Name;

    //            optionsBuilder.UseSqlServer(con);

    //            base.OnConfiguring(optionsBuilder);
    //        }
    //        protected override void OnModelCreating(ModelBuilder modelBuilder)
    //        {
    //            var t = modelBuilder.Entity<T>().HasNoKey();

    //            //to support anonymous types, configure entity properties for read-only properties
    //            foreach (var prop in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public))
    //            {
    //                if (prop.CustomAttributes.All(a => a.AttributeType != typeof(NotMappedAttribute)))
    //                    t.Property(prop.Name);
    //            }

    //            base.OnModelCreating(modelBuilder);
    //        }
    //    }
    //}
}
