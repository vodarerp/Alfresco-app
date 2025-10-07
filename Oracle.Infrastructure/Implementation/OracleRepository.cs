using Dapper;
using Oracle.Abstraction.Interfaces;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Runtime.Intrinsics.Arm;
using System.Threading;
using Oracle.Infrastructure.Helpers;
using System.Data;



namespace Oracle.Infrastructure.Implementation
{
    public class OracleRepository<T, TKey> : IRepository<T, TKey>
    {
        //protected readonly OracleConnection _connection;
        //protected readonly OracleTransaction _transaction;
        protected readonly IUnitOfWork _unitOfWork;
        protected IDbConnection Conn => _unitOfWork.Connection;
        protected IDbTransaction Tx => _unitOfWork.Transaction;

        public OracleRepository(IUnitOfWork uow) => _unitOfWork = uow;
        


        //public OracleRepository(OracleConnection connection, OracleTransaction transaction)
        //{
        //    _connection = connection;
        //    _transaction = transaction;
        //}

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

            await Conn.ExecuteAsync(new CommandDefinition(sql, parameters, Tx, cancellationToken: cancellationToken)).ConfigureAwait(false);
            var outVal = parameters.Get<long>($":outId");


            return (TKey)Convert.ChangeType(outVal, typeof(TKey)); ;
        }
        public async Task<int> InsertManyAsync(IEnumerable<T> entities, CancellationToken ct = default)
        {
            var listEntities = entities.ToList();
            if (listEntities.Count == 0) return 0;

            var columns = OracleHelpers<T>.TableProps.Where(o => !o.IsIdentity).ToArray();
            var colNames = string.Join(", ", columns.Select(c => c.Col));
            var paramNames = string.Join(", ", columns.Select(c => $":{c.Col}"));

            string sql = $"INSERT INTO {TableName} ({colNames}) VALUES ({paramNames})";

            var batchSize = 1000;
            int totalInserted = 0;

            for (int offset = 0; offset < listEntities.Count; offset += batchSize)
            {
                ct.ThrowIfCancellationRequested();

                var batch = listEntities.Skip(offset).Take(batchSize).ToList();
                var count = batch.Count;

                // Use Oracle array binding for bulk insert
                using var cmd = (OracleCommand)Conn.CreateCommand();
                cmd.Transaction = (OracleTransaction)Tx;
                cmd.BindByName = true;
                cmd.ArrayBindCount = count;
                cmd.CommandText = sql;

                // Prepare arrays for each column
                foreach (var col in columns)
                {
                    var values = batch.Select(e => col.Prop.GetValue(e)).ToArray();
                    var oracleType = GetOracleDbType(col.Prop.PropertyType);
                    cmd.Parameters.Add($":{col.Col}", oracleType, values, ParameterDirection.Input);
                }

                var inserted = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                totalInserted += inserted;
            }

            return totalInserted;
        }

        private static OracleDbType GetOracleDbType(Type type)
        {
            return Type.GetTypeCode(type) switch
            {
                TypeCode.Int16 => OracleDbType.Int16,
                TypeCode.Int32 => OracleDbType.Int32,
                TypeCode.Int64 => OracleDbType.Int64,
                TypeCode.Decimal => OracleDbType.Decimal,
                TypeCode.Double => OracleDbType.Double,
                TypeCode.Single => OracleDbType.Single,
                TypeCode.DateTime => OracleDbType.TimeStamp,
                TypeCode.String => OracleDbType.Varchar2,
                TypeCode.Boolean => OracleDbType.Byte,
                _ => OracleDbType.Varchar2
            };
        }

        public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
        {
            var key = OracleHelpers<T>.TableProps.Where(o => o.IsKey).FirstOrDefault();

            var sql = $"DELETE FROM {TableName} WHERE {key.Col} = :id";

            var dp = new DynamicParameters();
            dp.Add(":id", id);

            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: cancellationToken);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task<T?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        {
            var key = OracleHelpers<T>.TableProps.Where(o => o.IsKey).FirstOrDefault();

            string sql = $"SELECT * FROM {TableName} WHERE {key.Col} = :id";

            var dp = new DynamicParameters();
            dp.Add(":id", id);
            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: cancellationToken);
            return await Conn.QueryFirstOrDefaultAsync<T>(cmd).ConfigureAwait(false);
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

            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);
            var res = await Conn.QueryAsync<T>(cmd).ConfigureAwait(false);

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

            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: cancellationToken);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }


    }
}
