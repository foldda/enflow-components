﻿using System.Collections.Generic;
using Foldda.DataAutomation.Framework;
using System.Threading;
using System;
using System.Threading.Tasks;
using Charian;
using System.Data;
using System.Data.OleDb;
using System.Linq;
using System.Globalization;
using System.IO;

namespace Foldda.DataAutomation.CsvHandler
{
    //Write to MS SQL Server using BulkCopy
    public abstract class BaseCsvDbTableHandler : BaseCsvHandler
    {

        public class DbTableConnectionConfig : Rda
        {
            public const string PARAM_DB_CONNECTION_STRING = "oledb-connection-string";
            public const string PARAM_DB_TABLE_NAME = "db-table-name";
            //public const string TABLE_COLUMNS_ARRAY = "table-columns";
            public const string PARAM_COLUMN_SPEC = "column-spec";
            public const string PARAM_PRE_PROCESSING_STORED_PROC = "pre-processing-stored-proc";
            public const string PARAM_POST_PROCESSING_STORED_PROC = "post-processing-stored-proc";

            public enum RDA_INDEX : int { DbConnectionString, DbTableName, ColumnSpec, PreProcessingStoredProc, PostProcessingStoredProc}

            internal string DbConnectionString
            {
                get => this[(int)RDA_INDEX.DbConnectionString].ScalarValue;
                set => this[(int)RDA_INDEX.DbConnectionString].ScalarValue = value.ToString();
            }
            internal string DbTableName
            {
                get => this[(int)RDA_INDEX.DbTableName].ScalarValue;
                set => this[(int)RDA_INDEX.DbTableName].ScalarValue = value.ToString();
            }
            internal string ColumnSpec
            {
                get => this[(int)RDA_INDEX.ColumnSpec].ScalarValue;
                set => this[(int)RDA_INDEX.ColumnSpec].ScalarValue = value.ToString();
            }
            internal string PreProcessingStoreProc
            {
                get => this[(int)RDA_INDEX.PreProcessingStoredProc].ScalarValue;
                set => this[(int)RDA_INDEX.PreProcessingStoredProc].ScalarValue = value.ToString();
            }
            internal string PostProcessingStoredProc
            {
                get => this[(int)RDA_INDEX.PostProcessingStoredProc].ScalarValue;
                set => this[(int)RDA_INDEX.PostProcessingStoredProc].ScalarValue = value.ToString();
            }

            public DbTableConnectionConfig(Rda eventContext)
            {
                this.FromRda(eventContext);
            }

            public DbTableConnectionConfig()
            {
            }
        }

        internal DbTableConnectionConfig LocalConfig { get; private set; }

        public override void SetParameters(IConfigProvider config)
        {
            /*
             * Set the default settings if these are unavailable from the input parameter records
             */

            LocalConfig = new DbTableConnectionConfig()
            {
                DbConnectionString = config.GetSettingValue(DbTableConnectionConfig.PARAM_DB_CONNECTION_STRING, string.Empty),
                DbTableName = config.GetSettingValue(DbTableConnectionConfig.PARAM_DB_TABLE_NAME, string.Empty)
            };

            try
            {
                _targetTableSchema = QueryTargetTableColumnNames(LocalConfig);   //get Database table schema if available
                _columnDefinitions.Clear();

                //get all columns specification (CSV index to column mappings)
                foreach (string specific in config.GetSettingValues(DbTableConnectionConfig.PARAM_COLUMN_SPEC))
                {
                    CsvColumnDataDefinition columnDef = CsvColumnDataDefinition.ParseSpec(specific);

                    if (_targetTableSchema.Count > 0 && !_targetTableSchema.Contains(columnDef.Name))
                    {
                        string report = string.Join(Environment.NewLine, _targetTableSchema);
                        string message = $"ERROR: Column '{columnDef.Name}' in config does not exist in target table '{LocalConfig.DbTableName}', valid colums are:\n {report}";
                        //Log(message);
                        throw new Exception(message);
                    }
                    else
                    {
                        //store the column-spec
                        Log($"Column '{columnDef.Name}' is verified against target table '{LocalConfig.DbTableName}'.");
                        _columnDefinitions.Add(columnDef.Name, columnDef);
                    }
                }
                Log($"Local setting verified OK - ConnString={LocalConfig.DbConnectionString} Table={LocalConfig.DbTableName}");
            }
            catch (Exception e)
            {
                Log($"ERROR in checking local setting ConnString={LocalConfig.DbConnectionString} Table={LocalConfig.DbTableName}: {e.Message}");
                Deb(e.StackTrace);
            }
        }


        /**
         * Defines each of the Csv columns and their corresponding database table column's name and data-type, in the
         * following specific format - 
         * 
         * <1-based Csv column index> | <db-table column's name>;<data-type>;<data-type parsing format>
         * 
         * eg "1|USER_AGE;int"
         * 
         * if unspecified, the default data type is varchar(max)
         * 
         * <Parameter>
         *  <Name>column-spec</Name>
         *  <Value>1|USER_AGE;int</Value>
         * </Parameter>
         * 
         * <Parameter>
         *  <Name>column-spec</Name>
         *  <Value>2|USER_HEIGHT;decimal</Value>
         * </Parameter>
         * <Parameter>
         *  <Name>column-spec</Name>
         *  <Value>3|ADDRESS;string;120</Value>
         * </Parameter>
         * 
         * <Parameter>
         *  <Name>column-spec</Name>
         *  <Value>4|USER_DOB;date-time;d/MM/yyyy H:mm</Value>
         * </Parameter>
         */

        internal Dictionary<string, CsvColumnDataDefinition> _columnDefinitions { get; set; } = new Dictionary<string, CsvColumnDataDefinition>();

        protected List<string> _targetTableSchema { get; set; } = new List<string>();

        internal void RunStoredProc(OleDbConnection connection, string dbStoredProc, string param1, string param2, string param3, string param4)
        {
            if(!string.IsNullOrEmpty(dbStoredProc))
            {
                using (OleDbCommand cmd = new OleDbCommand(dbStoredProc, connection))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    if (!string.IsNullOrEmpty(param1)) { cmd.Parameters.Add(param1); }
                    if (!string.IsNullOrEmpty(param2)) { cmd.Parameters.Add(param2); }
                    if (!string.IsNullOrEmpty(param3)) { cmd.Parameters.Add(param3); }
                    if (!string.IsNullOrEmpty(param4)) { cmd.Parameters.Add(param4); }
                    
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public BaseCsvDbTableHandler(ILoggingProvider logger, DirectoryInfo homePath) : base(logger, homePath) { }

        private List<string> QueryTargetTableColumnNames(DbTableConnectionConfig config)
        {
            List<string> result = new List<string>();
            try
            {
                using (OleDbConnection connection = new OleDbConnection(config.DbConnectionString))
                {
                    Deb($"OLE-DB connection string = [{config.DbConnectionString}]");
                    connection.Open();
                    //get target table's column names - 
                    //https://stackoverflow.com/questions/34517470/retrieve-column-names-and-types-from-sql-server-to-datatable-c-sharp

                    //dynamically retrieve the table's schema via a query ...
                    using (var selectCommand = new OleDbCommand($"SELECT * FROM [{config.DbTableName}] WHERE 1 = 0", connection))
                    {
                        using (OleDbDataReader reader = selectCommand.ExecuteReader(CommandBehavior.SingleRow))
                        {
                            var dataColumns = Enumerable.Range(0, reader.FieldCount)
                                                        .Select(i => reader.GetName(i))
                                                        .ToArray();

                            result.AddRange(dataColumns);
                        }
                    }
                }
            }
            catch
            {
                Log($"ERROR: cannot connect to table '{config.DbTableName}' via connaction string -\n'{config.DbConnectionString}'");
            }

            return result;
        }



        //used by OLE_DB Writer/Reader handler, for capturing each column's corresponding database data-types
        internal class CsvColumnDataDefinition : Rda
        {
            enum RDA_INDEX : int { CSV_COLUMN_INDEX, DB_COLUMN_NAME, DATA_TYPE, FORMAT }

            public const string TYPE_INT = "integer";   //integer
            public const string TYPE_DEC = "decimal";   //decimal
            public const string TYPE_DT = "date-time";   //date-time
            public const string TYPE_STRING = "string";   //varchar

            const int INVALID_INDEX = -1;

            public int CsvColumnIndex //{ get; set; }
            {
                get => int.TryParse(this[(int)RDA_INDEX.CSV_COLUMN_INDEX].ScalarValue, out int value) ? value : INVALID_INDEX;
                set => this[(int)RDA_INDEX.CSV_COLUMN_INDEX].ScalarValue = value.ToString();
            }

            public string Name //{ get; set; }
            {
                get => this[(int)RDA_INDEX.DB_COLUMN_NAME].ScalarValue;
                set => this[(int)RDA_INDEX.DB_COLUMN_NAME].ScalarValue = value;
            }
            public string DataType //{ get; set; }//
            {
                get => this[(int)RDA_INDEX.DATA_TYPE].ScalarValue;
                set => this[(int)RDA_INDEX.DATA_TYPE].ScalarValue = value;
            }
            public string Format //{ get; set; }
            {
                get => this[(int)RDA_INDEX.FORMAT].ScalarValue;
                set => this[(int)RDA_INDEX.FORMAT].ScalarValue = value;
            }
            public OleDbType DbType => GetColumnOleDbType(DataType);

            const string DEFAULT_DATE_TIME_FORMAT = "yyyy-MM-dd HH:mm:ss";

            public object ParseValueToColumnTypedObject(string value)
            {
                if (this.DbType == OleDbType.DBTimeStamp)
                {
                    //may throw parsing error.
                    string format = string.IsNullOrEmpty(Format) ? DEFAULT_DATE_TIME_FORMAT : Format;
                    return DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result) ?
                        (object)result : DBNull.Value;
                }
                else if (this.DbType == OleDbType.Integer)
                {
                    return Int32.TryParse(value, out int outInt) ? (object)outInt : DBNull.Value;
                }
                else if (this.DbType == OleDbType.Decimal)
                {
                    return Decimal.TryParse(value, out decimal outDec) ? (object)outDec : DBNull.Value;
                }
                else /* string */
                {
                    //truncate the string if max-length is specified
                    if (!string.IsNullOrEmpty(this.Format) && int.TryParse(Format, out int length) && value.Length > length)
                    {
                        return value.Substring(0, length);
                    }
                    else
                    {
                        return value;
                    }
                }
            }

            public static string ApplyFormat(object oleDbObjectValue, CsvColumnDataDefinition columnDef)
            {
                if(oleDbObjectValue == null || oleDbObjectValue.GetType() == typeof(DBNull))
                {
                    return string.Empty;
                }
                else if(TYPE_DT.Equals(columnDef.DataType))
                {
                    return ((DateTime)oleDbObjectValue).ToString(columnDef.Format);
                }
                else
                {
                    return oleDbObjectValue.ToString();
                }
            }

            private static OleDbType GetColumnOleDbType(string columnTypeString)
            {
                switch (columnTypeString)
                {
                    case TYPE_INT: { return OleDbType.Integer; }
                    case TYPE_DEC: { return OleDbType.Decimal; }
                    case TYPE_DT: { return OleDbType.DBTimeStamp; }
                    case TYPE_STRING: { return OleDbType.VarChar; }
                    default: { return OleDbType.VarChar; }
                }
            }

            /// <summary>
            /// <1-based Csv column index> | <db-table column's name>;<data-type>;<data-type parsing format>
            /// eg "1|USER_AGE;int"
            /// </summary>
            /// <param name="specific"></param>
            /// <returns></returns>

            internal static CsvColumnDataDefinition ParseSpec(string specific)
            {
                Rda rda = Rda.Parse("|;\\|" + specific);    //makes it an Rda-formatted string for parsing.

                return new CsvColumnDataDefinition(rda);
            }

            internal CsvColumnDataDefinition(Rda rda)
            {
                CsvColumnIndex = Int32.Parse(rda[0].ScalarValue);
                Name = rda[1, 0].ScalarValue;
                DataType = rda[1, 1].ScalarValue;
                Format = rda[1, 2].ScalarValue;
            }
        }
    }
}