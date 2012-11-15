﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;

namespace Dapper
{
    public static partial class SqlMapper
    {
        partial class GridReader
        {
            public async Task<IList<T>> ReadListAsync<T>()
            {
                return await Task.Run(
                    async
                    () =>
                        {
                            if (reader == null)
                                throw new ObjectDisposedException(
                                    GetType().FullName,
                                    "The reader has been disposed; this can happen after all data has been consumed"
                                    );
                            if (consumed)
                                throw new InvalidOperationException("Each grid can only be iterated once");

                            var index = gridIndex;
                            try
                            {

                                var typedIdentity = identity.ForGrid(typeof (T), gridIndex);
                                var cache = GetCacheInfo(typedIdentity);
                                var deserializer = cache.Deserializer;

                                var hash = GetColumnHash(reader);
                                if (deserializer.Func == null || deserializer.Hash != hash)
                                {
                                    deserializer = new DeserializerState(hash,
                                                                        GetDeserializer(typeof (T), reader, 0,
                                                                                        -1, false));
                                    cache.Deserializer = deserializer;
                                }
                                consumed = true;

                                IList<T> result = new List<T>();
                                var dbDataReader = (DbDataReader) reader;
                                while (index == gridIndex && await dbDataReader.ReadAsync())
                                {
                                    result.Add(
                                        (T) deserializer.Func(reader)
                                        );
                                }
                                return result;
                            }
                            finally // finally so that First etc progresses things even when multiple rows
                            {
                                if (index == gridIndex)
                                {
                                    NextResult();
                                }
                            }
                        }
                                );
            }
        }

            /// <summary>
            /// Query Async Multiple
            /// </summary>
            /// <param name="cnn"></param>
            /// <param name="sql"></param>
            /// <param name="param"></param>
            /// <param name="transaction"></param>
            /// <param name="commandTimeout"></param>
            /// <param name="commandType"></param>
            /// <returns></returns>
            public static async Task<GridReader> QueryMultipleAsync(
                this IDbConnection cnn,
                string sql,
                dynamic param = null,
                IDbTransaction transaction = null,
                int? commandTimeout = null,
                CommandType? commandType = null
                )
            {
                var identity = new Identity(
                    sql,
                    commandType,
                    cnn,
                    typeof (GridReader),
                    (object) param == null
                        ? null
                        : ((object) param).GetType(),
                    null
                    );
                var info = GetCacheInfo(identity);

                DbCommand cmd = null;
                DbDataReader reader = null;
                var wasClosed = cnn.State == ConnectionState.Closed;
                var commandBehavior = wasClosed
                                          ? CommandBehavior.CloseConnection
                                          : CommandBehavior.Default;
                try
                {
                    if (wasClosed) cnn.Open();
                    cmd = (DbCommand)
                          SetupCommand(
                              cnn,
                              transaction,
                              sql,
                              info.ParamReader,
                              param,
                              commandTimeout,
                              commandType
                              );
                    
                    reader = await cmd.ExecuteReaderAsync(commandBehavior);

                    var result = new GridReader(cmd, reader, identity);
                    wasClosed = false; // *if* the connection was closed and we got this far, then we now have a reader
                    // with the CloseConnection flag, so the reader will deal with the connection; we
                    // still need something in the "finally" to ensure that broken SQL still results
                    // in the connection closing itself
                    return result;
                }
                catch
                {
                    if (reader != null)
                    {
                        if (!reader.IsClosed)
                            try
                            {
                                cmd.Cancel();
                            }
                            catch
                            {
                                /* don't spol the existing exception */
                            }
                        reader.Dispose();
                    }
                    if (cmd != null) cmd.Dispose();
                    if (wasClosed) cnn.Close();
                    throw;
                }
            }
        /// <summary>
        /// Execute a query asynchronously using .NET 4.5 Task.
        /// </summary>
        public static async Task<IEnumerable<T>> QueryAsync<T>(this IDbConnection cnn, string sql, dynamic param = null, IDbTransaction transaction = null, int? commandTimeout = null, CommandType? commandType = null)
        {
            var identity = new Identity(sql, commandType, cnn, typeof(T), param == null ? null : param.GetType(), null);
            var info = GetCacheInfo(identity);
            var cmd = (DbCommand)SetupCommand(cnn, transaction, sql, info.ParamReader, param, commandTimeout, commandType);

            using (var reader = await cmd.ExecuteReaderAsync())
            {
                return ExecuteReader<T>(reader, identity, info).ToList();
            }
        }

        /// <summary>
        /// Maps a query to objects
        /// </summary>
        /// <typeparam name="TFirst">The first type in the recordset</typeparam>
        /// <typeparam name="TSecond">The second type in the recordset</typeparam>
        /// <typeparam name="TReturn">The return type</typeparam>
        /// <param name="cnn"></param>
        /// <param name="sql"></param>
        /// <param name="map"></param>
        /// <param name="param"></param>
        /// <param name="transaction"></param>
        /// <param name="buffered"></param>
        /// <param name="splitOn">The Field we should split and read the second object from (default: id)</param>
        /// <param name="commandTimeout">Number of seconds before command execution timeout</param>
        /// <param name="commandType">Is it a stored proc or a batch?</param>
        /// <returns></returns>
        public static async Task<IEnumerable<TReturn>> QueryAsync<TFirst, TSecond, TReturn>(this IDbConnection cnn, string sql, Func<TFirst, TSecond, TReturn> map, dynamic param = null, IDbTransaction transaction = null, bool buffered = true, string splitOn = "Id", int? commandTimeout = null, CommandType? commandType = null)
        {
            return await MultiMapAsync<TFirst, TSecond, DontMap, DontMap, DontMap, DontMap, DontMap, TReturn>(cnn, sql, map, param as object, transaction, buffered, splitOn, commandTimeout, commandType);
        }

        /// <summary>
        /// Maps a query to objects
        /// </summary>
        /// <typeparam name="TFirst"></typeparam>
        /// <typeparam name="TSecond"></typeparam>
        /// <typeparam name="TThird"></typeparam>
        /// <typeparam name="TReturn"></typeparam>
        /// <param name="cnn"></param>
        /// <param name="sql"></param>
        /// <param name="map"></param>
        /// <param name="param"></param>
        /// <param name="transaction"></param>
        /// <param name="buffered"></param>
        /// <param name="splitOn">The Field we should split and read the second object from (default: id)</param>
        /// <param name="commandTimeout">Number of seconds before command execution timeout</param>
        /// <param name="commandType"></param>
        /// <returns></returns>
        public static async Task<IEnumerable<TReturn>> QueryAsync<TFirst, TSecond, TThird, TReturn>(this IDbConnection cnn, string sql, Func<TFirst, TSecond, TThird, TReturn> map, dynamic param = null, IDbTransaction transaction = null, bool buffered = true, string splitOn = "Id", int? commandTimeout = null, CommandType? commandType = null)
        {
            return await MultiMapAsync<TFirst, TSecond, TThird, DontMap, DontMap, DontMap, DontMap, TReturn>(cnn, sql, map, param as object, transaction, buffered, splitOn, commandTimeout, commandType);
        }

        /// <summary>
        /// Perform a multi mapping query with 4 input parameters
        /// </summary>
        /// <typeparam name="TFirst"></typeparam>
        /// <typeparam name="TSecond"></typeparam>
        /// <typeparam name="TThird"></typeparam>
        /// <typeparam name="TFourth"></typeparam>
        /// <typeparam name="TReturn"></typeparam>
        /// <param name="cnn"></param>
        /// <param name="sql"></param>
        /// <param name="map"></param>
        /// <param name="param"></param>
        /// <param name="transaction"></param>
        /// <param name="buffered"></param>
        /// <param name="splitOn"></param>
        /// <param name="commandTimeout"></param>
        /// <param name="commandType"></param>
        /// <returns></returns>
        public static async Task<IEnumerable<TReturn>> QueryAsync<TFirst, TSecond, TThird, TFourth, TReturn>(this IDbConnection cnn, string sql, Func<TFirst, TSecond, TThird, TFourth, TReturn> map, dynamic param = null, IDbTransaction transaction = null, bool buffered = true, string splitOn = "Id", int? commandTimeout = null, CommandType? commandType = null)
        {
            return await MultiMapAsync<TFirst, TSecond, TThird, TFourth, DontMap, DontMap, DontMap, TReturn>(cnn, sql, map, param as object, transaction, buffered, splitOn, commandTimeout, commandType);
        }

        /// <summary>
        /// Perform a multi mapping query with 5 input parameters
        /// </summary>
        /// <typeparam name="TFirst"></typeparam>
        /// <typeparam name="TSecond"></typeparam>
        /// <typeparam name="TThird"></typeparam>
        /// <typeparam name="TFourth"></typeparam>
        /// <typeparam name="TFifth"></typeparam>
        /// <typeparam name="TReturn"></typeparam>
        /// <param name="cnn"></param>
        /// <param name="sql"></param>
        /// <param name="map"></param>
        /// <param name="param"></param>
        /// <param name="transaction"></param>
        /// <param name="buffered"></param>
        /// <param name="splitOn"></param>
        /// <param name="commandTimeout"></param>
        /// <param name="commandType"></param>
        /// <returns></returns>
        public static async Task<IEnumerable<TReturn>> QueryAsync<TFirst, TSecond, TThird, TFourth, TFifth, TReturn>(this IDbConnection cnn, string sql, Func<TFirst, TSecond, TThird, TFourth, TFifth, TReturn> map, dynamic param = null, IDbTransaction transaction = null, bool buffered = true, string splitOn = "Id", int? commandTimeout = null, CommandType? commandType = null)
        {
            return await MultiMapAsync<TFirst, TSecond, TThird, TFourth, TFifth, DontMap, DontMap, TReturn>(cnn, sql, map, param as object, transaction, buffered, splitOn, commandTimeout, commandType);
        }

        /// <summary>
        /// Perform a multi mapping query with 6 input parameters
        /// </summary>
        /// <typeparam name="TFirst"></typeparam>
        /// <typeparam name="TSecond"></typeparam>
        /// <typeparam name="TThird"></typeparam>
        /// <typeparam name="TFourth"></typeparam>
        /// <typeparam name="TFifth"></typeparam>
        /// <typeparam name="TSixth"></typeparam>
        /// <typeparam name="TReturn"></typeparam>
        /// <param name="cnn"></param>
        /// <param name="sql"></param>
        /// <param name="map"></param>
        /// <param name="param"></param>
        /// <param name="transaction"></param>
        /// <param name="buffered"></param>
        /// <param name="splitOn"></param>
        /// <param name="commandTimeout"></param>
        /// <param name="commandType"></param>
        /// <returns></returns>
        public static async Task<IEnumerable<TReturn>> QueryAsync<TFirst, TSecond, TThird, TFourth, TFifth, TSixth, TReturn>(this IDbConnection cnn, string sql, Func<TFirst, TSecond, TThird, TFourth, TFifth, TSixth, TReturn> map, dynamic param = null, IDbTransaction transaction = null, bool buffered = true, string splitOn = "Id", int? commandTimeout = null, CommandType? commandType = null)
        {
            return await MultiMapAsync<TFirst, TSecond, TThird, TFourth, TFifth, TSixth, DontMap, TReturn>(cnn, sql, map, param as object, transaction, buffered, splitOn, commandTimeout, commandType);
        }

        /// <summary>
        /// Perform a multi mapping query with 7 input parameters
        /// </summary>
        /// <typeparam name="TFirst"></typeparam>
        /// <typeparam name="TSecond"></typeparam>
        /// <typeparam name="TThird"></typeparam>
        /// <typeparam name="TFourth"></typeparam>
        /// <typeparam name="TFifth"></typeparam>
        /// <typeparam name="TSixth"></typeparam>
        /// <typeparam name="TSeventh"></typeparam>
        /// <typeparam name="TReturn"></typeparam>
        /// <param name="cnn"></param>
        /// <param name="sql"></param>
        /// <param name="map"></param>
        /// <param name="param"></param>
        /// <param name="transaction"></param>
        /// <param name="buffered"></param>
        /// <param name="splitOn"></param>
        /// <param name="commandTimeout"></param>
        /// <param name="commandType"></param>
        /// <returns></returns>
        public static async Task<IEnumerable<TReturn>> QueryAsync<TFirst, TSecond, TThird, TFourth, TFifth, TSixth, TSeventh, TReturn>(this IDbConnection cnn, string sql, Func<TFirst, TSecond, TThird, TFourth, TFifth, TSixth, TSeventh, TReturn> map, dynamic param = null, IDbTransaction transaction = null, bool buffered = true, string splitOn = "Id", int? commandTimeout = null, CommandType? commandType = null)
        {
            return await MultiMapAsync<TFirst, TSecond, TThird, TFourth, TFifth, TSixth, TSeventh, TReturn>(cnn, sql, map, param as object, transaction, buffered, splitOn, commandTimeout, commandType);
        }

        static async Task<IEnumerable<TReturn>> MultiMapAsync<TFirst, TSecond, TThird, TFourth, TFifth, TSixth, TSeventh, TReturn>(this IDbConnection cnn, string sql, object map, object param, IDbTransaction transaction, bool buffered, string splitOn, int? commandTimeout, CommandType? commandType)
        {
            var identity = new Identity(sql, commandType, cnn, typeof(TFirst), (object)param == null ? null : ((object)param).GetType(), new[] { typeof(TFirst), typeof(TSecond), typeof(TThird), typeof(TFourth), typeof(TFifth), typeof(TSixth), typeof(TSeventh) });
            var info = GetCacheInfo(identity);
            var cmd = (DbCommand)SetupCommand(cnn, transaction, sql, info.ParamReader, param, commandTimeout, commandType);
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                var results = MultiMapImpl<TFirst, TSecond, TThird, TFourth, TFifth, TSixth, TSeventh, TReturn>(null, null, map, null, null, splitOn, null, null, reader, identity);
                return buffered ? results.ToList() : results;
            }
        }

        private static IEnumerable<T> ExecuteReader<T>(IDataReader reader, Identity identity, CacheInfo info)
        {
            var tuple = info.Deserializer;
            int hash = GetColumnHash(reader);
            if (tuple.Func == null || tuple.Hash != hash)
            {
                tuple = info.Deserializer = new DeserializerState(hash, GetDeserializer(typeof(T), reader, 0, -1, false));
                SetQueryCache(identity, info);
            }

            var func = tuple.Func;

            while (reader.Read())
            {
                yield return (T)func(reader);
            }
        }
    }
}