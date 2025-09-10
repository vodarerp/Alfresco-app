using Dapper;
using Oracle.Apstaction.Interfaces;
using Oracle.ManagedDataAccess.Client;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Runtime.Intrinsics.Arm;
using System.Threading;
using Oracle.Infractructure.Helpers;



namespace Oracle.Infractructure.Implementation
{
    public class OracleRepository<T, TKey> : IRepository<T, TKey>
    {
        internal readonly OracleConnection _connection;
        internal readonly OracleTransaction _transaction;
        
        public OracleRepository(OracleConnection connection, OracleTransaction transaction)
        {
            _connection = connection;
            _transaction = transaction;
        }

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

            //var columns = GetColumns().Where(o => !o.IsIdentity).ToArray();
            var columns = OracleHelpers<T>.TableProps.Where(o => !o.IsIdentity).ToArray();
            var colNames = string.Join(", ", columns.Select(c => c.Col));
            var paramNames = string.Join(", ", columns.Select(c => $":{c.Col}"));
            var idCol = GetColumns().FirstOrDefault(o => o.IsKey).Col ?? "Id";

            string sql = $"INSERT INTO appUser.{TableName} ({colNames}) VALUES ({paramNames}) RETURNING {idCol} INTO :outId";
            var parameters = new DynamicParameters();
            foreach (var (Prop, Col, IsKey, IsIdentity) in columns)
            {
                var val = Prop.GetValue(entity);
                parameters.Add($":{Col}", val);
            }
            parameters.Add($":outId", dbType: System.Data.DbType.Int64, direction: System.Data.ParameterDirection.Output);

            await _connection.ExecuteAsync(new CommandDefinition(sql, parameters, _transaction, cancellationToken: cancellationToken));
            var outVal = parameters.Get<long>($":outId");


            return (TKey)Convert.ChangeType(outVal, typeof(TKey)); ;
        }
        public async Task<int> InsertManyAsync(IEnumerable<T> entities, CancellationToken ct = default)
        {
            int toRet = -1;
            var listEntities = entities.ToList();
            //var columns = GetColumns().Where(o => !o.IsIdentity).ToArray();
            var columns = OracleHelpers<T>.TableProps.Where(o => !o.IsIdentity).ToArray();
            var colNames = string.Join(", ", columns.Select(c => c.Col));
            var paramNames = string.Join(", ", columns.Select(c => $":{c.Col}"));
            var idCol = GetColumns().FirstOrDefault(o => o.IsKey).Col ?? "Id";

            string sql = $"INSERT INTO appUser.{TableName} ({colNames}) VALUES ({paramNames})";

            var batchSize = 1000; // promeni da se cita iz OravleOptions.... OravleOptions dodati kroz DI kontejner

            for (int offset = 0; offset < listEntities.Count(); offset += batchSize)
            {
                var forInsert = listEntities.Skip(offset).Take(batchSize);

                foreach(var o in forInsert)
                {
                    ct.ThrowIfCancellationRequested();

                    var param = new DynamicParameters();
                    foreach(var c in columns)
                    {
                        param.Add(c.Col, c.Prop.GetValue(o));
                    }

                    var cmd = new CommandDefinition(sql, param, _transaction, 100, cancellationToken: ct);
                    toRet = await _connection.ExecuteAsync(cmd).ConfigureAwait(false);
                }
            }


            return toRet;
        }

        public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
        {
            var key = OracleHelpers<T>.TableProps.Where(o => o.IsKey).FirstOrDefault();

            var sql = $"DELETE FROM {TableName} WHERE {key.Col} = :id";

            var dp = new DynamicParameters();
            dp.Add(":id", id);

            var cmd = new CommandDefinition(sql, dp, _transaction, cancellationToken: cancellationToken);
            await _connection.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task<T?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        {
            var key = OracleHelpers<T>.TableProps.Where(o => o.IsKey).FirstOrDefault();

            string sql = $"SELECT * FROM {TableName} WHERE {key.Col} = :id";

            var dp = new DynamicParameters();
            dp.Add(":id", id);
            var cmd = new CommandDefinition(sql, dp, _transaction, cancellationToken: cancellationToken);
            return await _connection.QueryFirstOrDefaultAsync<T>(cmd).ConfigureAwait(false);
        }

        public async Task<IEnumerable<T>> GetListAsync(object? filters = null, int? skip = null, int? take = null, string[]? orderBy = null, CancellationToken ct = default)
        {

            //var cnt = await _connection.ExecuteScalarAsync<int>("select  count(*) from dual");

           // var cnt1 = await _connection.ExecuteScalarAsync<int>("select  count(*) from appUser.DOCSTAGING");

            var (whereSql, dp) = OracleHelpers<T>.BuildWhere(filters);

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
                sql += $" OFFSET {s} ROWS FETCH NEXT {t} ROWS ONLY ";
            }

            //string sql = $"SELECT * FROM {TableName}";
            //var res = await _connection.QueryAsync<T>(new CommandDefinition(sql, cancellationToken: ct));

            var cmd = new CommandDefinition(sql, dp, _transaction, cancellationToken: ct);
            var res = await _connection.QueryAsync<T>(cmd).ConfigureAwait(false);

            return res;

        }

       

        public async Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
        {
            var key = OracleHelpers<T>.TableProps.Where(o => o.IsKey).FirstOrDefault();
            
            var setCols = OracleHelpers<T>.TableProps.Where(o => !o.IsKey && !o.IsIdentity).ToArray();

            if (setCols.Count() == 0) return;

            var setPart = string.Join(", ", setCols.Select(c => $"{c.Col} = :{c.Col}"));
            string sql = $"UPDATE {TableName} SET {setPart} WHERE {key.Col} = :{key.Col}";

            var dp = new DynamicParameters();

            foreach (var (Prop, Col, IsKey, IsIdentity) in setCols)
            {
                var val = Prop.GetValue(entity);
                dp.Add($":{Col}", val);
            }

            var cmd = new CommandDefinition(sql, dp, _transaction, cancellationToken: cancellationToken);
            await _connection.ExecuteAsync(cmd).ConfigureAwait(false);
        }


    }
}
