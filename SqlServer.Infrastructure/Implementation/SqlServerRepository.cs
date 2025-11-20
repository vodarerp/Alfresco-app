using Dapper;
using SqlServer.Abstraction.Interfaces;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Threading;
using SqlServer.Infrastructure.Helpers;
using System.Data;



namespace SqlServer.Infrastructure.Implementation
{
    public class SqlServerRepository<T, TKey> : IRepository<T, TKey>
    {
        protected readonly IUnitOfWork _unitOfWork;

        // Get connection - if UnitOfWork has active transaction use it, otherwise throw
        protected IDbConnection Conn
        {
            get
            {
                // UnitOfWork.Connection will throw if connection not initialized
                // This is intentional - we want operations to fail fast if UnitOfWork not properly initialized
                return _unitOfWork.Connection;
            }
        }

        protected IDbTransaction? Tx => _unitOfWork.Transaction;

        public SqlServerRepository(IUnitOfWork uow) => _unitOfWork = uow;


        public string TableName => typeof(T).GetCustomAttributes<TableAttribute>()?.FirstOrDefault()?.Name ?? typeof(T).Name;


        private IEnumerable<(PropertyInfo Prop, string Col, bool IsKey, bool IsIdentity)> GetColumns()
        {
            foreach (var p in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var col = p.GetCustomAttribute<ColumnAttribute>()?.Name ?? p.Name;
                var key = p.GetCustomAttribute<KeyAttribute>() is not null || string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase);
                var identity = p.GetCustomAttribute<DatabaseGeneratedAttribute>()?.DatabaseGeneratedOption == DatabaseGeneratedOption.Identity;
                yield return (p, col, key, identity);
            }
        }

        public async Task<TKey> AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            var columns = SqlServerHelpers<T>.TableProps.Where(o => !o.IsIdentity).ToArray();
            var colNames = string.Join(", ", columns.Select(c => c.Col));
            var paramNames = string.Join(", ", columns.Select(c => $"@{c.Col}"));
            var idCol = GetColumns().FirstOrDefault(o => o.IsKey).Col ?? "Id";

            // SQL Server uses OUTPUT instead of RETURNING
            string sql = $"INSERT INTO {TableName} ({colNames}) OUTPUT INSERTED.{idCol} VALUES ({paramNames})";
            var parameters = new DynamicParameters();
            foreach (var (Prop, Col, IsKey, IsIdentity) in columns)
            {
                var val = Prop.GetValue(entity);
                parameters.Add($"@{Col}", val);
            }

            var outVal = await Conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, parameters, Tx, cancellationToken: cancellationToken)).ConfigureAwait(false);

            return (TKey)Convert.ChangeType(outVal, typeof(TKey));
        }

        public async Task<int> InsertManyAsync(IEnumerable<T> entities, CancellationToken ct = default)
        {
            var listEntities = entities.ToList();
            if (listEntities.Count == 0) return 0;

            var columns = SqlServerHelpers<T>.TableProps.Where(o => !o.IsIdentity).ToArray();
            var colNames = string.Join(", ", columns.Select(c => c.Col));

            // Batch size for multi-row INSERT statements
            // SQL Server has a limit of 2100 parameters per query
            // Calculate safe batch size: 2000 / number of columns (with safety margin)
            var maxRowsPerBatch = Math.Max(1, 2000 / columns.Length);
            var batchSize = Math.Min(1000, maxRowsPerBatch);
            int totalInserted = 0;

            for (int offset = 0; offset < listEntities.Count; offset += batchSize)
            {
                ct.ThrowIfCancellationRequested();

                var batch = listEntities.Skip(offset).Take(batchSize).ToList();

                // Build multi-row INSERT statement
                // Example: INSERT INTO Table (col1, col2) VALUES (@col1_0, @col2_0), (@col1_1, @col2_1), ...
                var valuesClauses = new List<string>();
                var dp = new DynamicParameters();

                for (int i = 0; i < batch.Count; i++)
                {
                    var entity = batch[i];
                    var paramList = new List<string>();

                    foreach (var col in columns)
                    {
                        var paramName = $"@{col.Col}_{i}";
                        var val = col.Prop.GetValue(entity);
                        dp.Add(paramName, val);
                        paramList.Add(paramName);
                    }

                    valuesClauses.Add($"({string.Join(", ", paramList)})");
                }

                // Single INSERT with multiple value rows
                string sql = $"INSERT INTO {TableName} ({colNames}) VALUES {string.Join(", ", valuesClauses)}";

                var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);
                totalInserted += await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
            }

            return totalInserted;
        }

        public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
        {
            var key = SqlServerHelpers<T>.TableProps.Where(o => o.IsKey).FirstOrDefault();

            var sql = $"DELETE FROM {TableName} WHERE {key.Col} = @id";

            var dp = new DynamicParameters();
            dp.Add("@id", id);

            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: cancellationToken);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task<T?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        {
            var key = SqlServerHelpers<T>.TableProps.Where(o => o.IsKey).FirstOrDefault();

            string sql = $"SELECT * FROM {TableName} WHERE {key.Col} = @id";

            var dp = new DynamicParameters();
            dp.Add("@id", id);
            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: cancellationToken);
            return await Conn.QueryFirstOrDefaultAsync<T>(cmd).ConfigureAwait(false);
        }

        public async Task<IEnumerable<T>> GetListAsync(object? filters = null, int? skip = null, int? take = null, string[]? orderBy = null, CancellationToken ct = default)
        {
            var (whereSql, dp) = SqlServerHelpers<T>.BuildWhere(filters);

            var sql = $"SELECT * FROM {TableName} {whereSql} ";

            if (orderBy != null && orderBy.Length > 0)
            {
                var orderSql = string.Join(", ", orderBy);
                sql += $" ORDER BY {orderSql} ";
            }

            if(skip.HasValue || take.HasValue)
            {
                int s = skip ?? 0;
                int t = take ?? 100;
                // SQL Server pagination syntax
                sql += $" OFFSET {s} ROWS FETCH NEXT {t} ROWS ONLY ";
            }

            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);
            var res = await Conn.QueryAsync<T>(cmd).ConfigureAwait(false);

            return res;
        }

        public async Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
        {
            var key = SqlServerHelpers<T>.TableProps.Where(o => o.IsKey).FirstOrDefault();

            var setCols = SqlServerHelpers<T>.TableProps.Where(o => !o.IsKey && !o.IsIdentity).ToArray();

            if (setCols.Count() == 0) return;

            var setPart = string.Join(", ", setCols.Select(c => $"{c.Col} = @{c.Col}"));
            string sql = $"UPDATE {TableName} SET {setPart} WHERE {key.Col} = @{key.Col}";

            var dp = new DynamicParameters();

            foreach (var (Prop, Col, IsKey, IsIdentity) in setCols)
            {
                var val = Prop.GetValue(entity);
                dp.Add($"@{Col}", val);
            }

            // Add key parameter
            var keyVal = key.Prop.GetValue(entity);
            dp.Add($"@{key.Col}", keyVal);

            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: cancellationToken);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }
    }
}
