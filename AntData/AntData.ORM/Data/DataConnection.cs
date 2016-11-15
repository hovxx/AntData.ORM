﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using JetBrains.Annotations;

namespace AntData.ORM.Data
{
	using System.Text;

	using Common;
	using Configuration;
	using DataProvider;

	using Mapping;

	public partial class DataConnection : ICloneable
	{
		#region .ctor

        public DataConnection()
            : this(null)
        {
        }

        public DataConnection(string configurationString)
        {
            InitConfig();

            ConfigurationString = configurationString ?? DefaultConfiguration;

            if (ConfigurationString == null)
                throw new LinqToDBException("Configuration string is not provided.");

            var ci = GetConfigurationInfo(ConfigurationString);

            DataProvider = ci.DataProvider;
            ConnectionString = ci.ConnectionString;
            _mappingSchema = DataProvider.MappingSchema;
        }

        //public DataConnection([JetBrains.Annotations.NotNull] string providerName, [JetBrains.Annotations.NotNull] string connectionString)
        //{
        //    if (providerName == null) throw new ArgumentNullException("providerName");
        //    if (connectionString == null) throw new ArgumentNullException("connectionString");

        //    IDataProvider dataProvider;

        //    if (!_dataProviders.TryGetValue(providerName, out dataProvider))
        //        throw new LinqToDBException("DataProvider '{0}' not found.".Args(providerName));

        //    InitConfig();

        //    DataProvider = dataProvider;
        //    ConnectionString = connectionString;
        //    _mappingSchema = DataProvider.MappingSchema;
        //}

        //public DataConnection([JetBrains.Annotations.NotNull] IDataProvider dataProvider, [JetBrains.Annotations.NotNull] string connectionString)
        //{
        //    if (dataProvider == null) throw new ArgumentNullException("dataProvider");
        //    if (connectionString == null) throw new ArgumentNullException("connectionString");

        //    InitConfig();

        //    DataProvider = dataProvider;
        //    _mappingSchema = DataProvider.MappingSchema;
        //    ConnectionString = connectionString;
        //}

        //public DataConnection([JetBrains.Annotations.NotNull] IDataProvider dataProvider, [JetBrains.Annotations.NotNull] IDbConnection connection)
        //{
        //    if (dataProvider == null) throw new ArgumentNullException("dataProvider");
        //    if (connection == null) throw new ArgumentNullException("connection");

        //    InitConfig();

        //    if (!Configuration.AvoidSpecificDataProviderAPI && !dataProvider.IsCompatibleConnection(connection))
        //        throw new LinqToDBException(
        //            "DataProvider '{0}' and connection '{1}' are not compatible.".Args(dataProvider, connection));

        //    DataProvider = dataProvider;
        //    _mappingSchema = DataProvider.MappingSchema;
        //    _connection = connection;
        //}

        //public DataConnection([JetBrains.Annotations.NotNull] IDataProvider dataProvider, [JetBrains.Annotations.NotNull] IDbTransaction transaction)
        //{
        //    if (dataProvider == null) throw new ArgumentNullException("dataProvider");
        //    if (transaction == null) throw new ArgumentNullException("transaction");

        //    InitConfig();

        //    if (!Configuration.AvoidSpecificDataProviderAPI && !dataProvider.IsCompatibleConnection(transaction.Connection))
        //        throw new LinqToDBException(
        //            "DataProvider '{0}' and connection '{1}' are not compatible.".Args(dataProvider, transaction.Connection));

        //    DataProvider = dataProvider;
        //    _mappingSchema = DataProvider.MappingSchema;
        //    _connection = transaction.Connection;
        //    Transaction = transaction;
        //    _closeTransaction = false;
        //}


        public DataConnection([JetBrains.Annotations.NotNull] IDataProvider dataProvider, string dbMappingName, Func<string, string, Dictionary<string, CustomerParam>, IDictionary, int> CustomerExecuteNonQuery, Func<string, string, Dictionary<string, CustomerParam>, IDictionary, object> CustomerExecuteScalar, Func<string, string, Dictionary<string, CustomerParam>, IDictionary, IDataReader> CustomerExecuteQuery, Func<string, string, Dictionary<string, CustomerParam>, IDictionary, DataTable> CustomerExecuteQueryTable)
        {
            if (dataProvider == null) throw new ArgumentNullException("dataProvider");

            InitConfig();

            DataProvider = dataProvider;
            _mappingSchema = DataProvider.MappingSchema;
            ConnectionString = dbMappingName;
            this.CustomerExecuteNonQuery = CustomerExecuteNonQuery;
            this.CustomerExecuteScalar = CustomerExecuteScalar;
            this.CustomerExecuteQuery = CustomerExecuteQuery;
            this.CustomerExecuteQueryTable = CustomerExecuteQueryTable;
        }
		#endregion

		#region Public Properties

		public string        ConfigurationString { get; private set; }
		public IDataProvider DataProvider        { get; private set; }
		public string        ConnectionString    { get; private set; }

		static readonly ConcurrentDictionary<string,int> _configurationIDs;
		static int _maxID;

		private int? _id;
		public  int   ID
		{
			get
			{
				if (!_id.HasValue)
				{
					var key = MappingSchema.ConfigurationID + "." + (ConfigurationString ?? ConnectionString ?? Connection.ConnectionString);
					int id;

					if (!_configurationIDs.TryGetValue(key, out id))
						_configurationIDs[key] = id = Interlocked.Increment(ref _maxID);

					_id = id;
				}

				return _id.Value;
			}
		}

		private bool? _isMarsEnabled;
		internal  bool   IsMarsEnabled
		{
			get
			{
				if (_isMarsEnabled == null)
					_isMarsEnabled = (bool)(DataProvider.GetConnectionInfo(this, "IsMarsEnabled") ?? false);

				return _isMarsEnabled.Value;
			}
			set { _isMarsEnabled = value; }
		}

		public static string DefaultConfiguration { get; set; }
		public static string DefaultDataProvider  { get; set; }

		private static Action<TraceInfo> _onTrace = OnTraceInternal;
		public  static Action<TraceInfo>  OnTrace
		{
			get { return _onTrace; }
			set { _onTrace = value ?? OnTraceInternal; }
		}

		private Action<TraceInfo> _onTraceConnection = OnTrace;
		[JetBrains.Annotations.CanBeNull]
		internal  Action<TraceInfo>  OnTraceConnection
		{
			get { return _onTraceConnection;  }
			set { _onTraceConnection = value; }
		}

        private Action<CustomerTraceInfo> _onCustomerTraceConnection;
		[JetBrains.Annotations.CanBeNull]
        public Action<CustomerTraceInfo> OnCustomerTraceConnection
		{
            get { return _onCustomerTraceConnection; }
            set { _onCustomerTraceConnection = value; }
		}



        private Func<string, string, Dictionary<string, CustomerParam>, IDictionary,int > CustomerExecuteNonQuery { get; set; }

        private Func<string, string, Dictionary<string, CustomerParam>, IDictionary, object> CustomerExecuteScalar { get; set; }

        private Func<string, string, Dictionary<string, CustomerParam>, IDictionary, IDataReader> CustomerExecuteQuery { get; set; }

        private Func<string, string, Dictionary<string, CustomerParam>, IDictionary, DataTable> CustomerExecuteQueryTable { get; set; }
	    static void OnTraceInternal(TraceInfo info)
		{
			if (info.BeforeExecute)
			{
				WriteTraceLine(info.SqlText, TraceSwitch.DisplayName);
			}
			else if (info.TraceLevel == TraceLevel.Error)
			{
				var sb = new StringBuilder();

				for (var ex = info.Exception; ex != null; ex = ex.InnerException)
				{
					sb
						.AppendLine()
						.AppendFormat("Exception: {0}", ex.GetType())
						.AppendLine()
						.AppendFormat("Message  : {0}", ex.Message)
						.AppendLine()
						.AppendLine(ex.StackTrace)
						;
				}

				WriteTraceLine(sb.ToString(), TraceSwitch.DisplayName);
			}
			else if (info.RecordsAffected != null)
			{
				WriteTraceLine("Execution time: {0}. Records affected: {1}.\r\n".Args(info.ExecutionTime, info.RecordsAffected), TraceSwitch.DisplayName);
			}
			else
			{
				WriteTraceLine("Execution time: {0}\r\n".Args(info.ExecutionTime), TraceSwitch.DisplayName);
			}
		}

		private static TraceSwitch _traceSwitch;
		public  static TraceSwitch  TraceSwitch
		{
			get
			{
				return _traceSwitch ?? (_traceSwitch = new TraceSwitch("DataConnection", "DataConnection trace switch",
#if DEBUG
				"Warning"
#else
				"Off"
#endif
				));
			}
			set { _traceSwitch = value; }
		}

        //public static void TurnTraceSwitchOn(TraceLevel traceLevel = TraceLevel.Info)
        //{
        //    TraceSwitch = new TraceSwitch("DataConnection", "DataConnection trace switch", traceLevel.ToString());
        //}

		public static Action<string,string> WriteTraceLine = (message, displayName) => Debug.WriteLine(message, displayName);

		#endregion

		#region Configuration

		static IDataProvider FindProvider(string configuration, IEnumerable<KeyValuePair<string,IDataProvider>> ps, IDataProvider defp)
		{
			foreach (var p in ps.OrderByDescending(kv => kv.Key.Length))
				if (configuration == p.Key || configuration.StartsWith(p.Key + '.'))
					return p.Value;

			foreach (var p in ps.OrderByDescending(kv => kv.Value.Name.Length))
				if (configuration == p.Value.Name || configuration.StartsWith(p.Value.Name + '.'))
					return p.Value;

			return defp;
		}

		static DataConnection()
		{
			_configurationIDs = new ConcurrentDictionary<string,int>();

            //LinqToDB.DataProvider.SqlServer. SqlServerTools. GetDataProvider();
            //LinqToDB.DataProvider.Access.    AccessTools.    GetDataProvider();
            //LinqToDB.DataProvider.SqlCe.     SqlCeTools.     GetDataProvider();
            //LinqToDB.DataProvider.Firebird.  FirebirdTools.  GetDataProvider();
            AntData.ORM.DataProvider.MySql.MySqlTools.GetDataProvider();
            //LinqToDB.DataProvider.SQLite.    SQLiteTools.    GetDataProvider();
            //LinqToDB.DataProvider.Sybase.    SybaseTools.    GetDataProvider();
            //LinqToDB.DataProvider.Oracle.    OracleTools.    GetDataProvider();
            //LinqToDB.DataProvider.PostgreSQL.PostgreSQLTools.GetDataProvider();
            //LinqToDB.DataProvider.DB2.       DB2Tools.       GetDataProvider();
            //LinqToDB.DataProvider.Informix.  InformixTools.  GetDataProvider();
            //LinqToDB.DataProvider.SapHana.   SapHanaTools.   GetDataProvider(); 

			var section = LinqToDBSection.Instance;

			if (section != null)
			{
				DefaultConfiguration = section.DefaultConfiguration;
				DefaultDataProvider  = section.DefaultDataProvider;

				foreach (DataProviderElement provider in section.DataProviders)
				{
					var dataProviderType = Type.GetType(provider.TypeName, true);
					var providerInstance = (IDataProviderFactory)Activator.CreateInstance(dataProviderType);

					if (!string.IsNullOrEmpty(provider.Name))
						AddDataProvider(provider.Name, providerInstance.GetDataProvider(provider.Attributes));
				}
			}
		}

		static readonly List<Func<ConnectionStringSettings,IDataProvider>> _providerDetectors =
			new List<Func<ConnectionStringSettings,IDataProvider>>();

		public static void AddProviderDetector(Func<ConnectionStringSettings,IDataProvider> providerDetector)
		{
			_providerDetectors.Add(providerDetector);
		}

		internal static bool IsMachineConfig(ConnectionStringSettings css)
		{
			string source;

			try
			{
				source = css.ElementInformation.Source;
			}
			catch (Exception)
			{
				source = "";
			}

			return source == null || source.EndsWith("machine.config", StringComparison.OrdinalIgnoreCase);
		}

		static void InitConnectionStrings()
		{
			foreach (ConnectionStringSettings css in ConfigurationManager.ConnectionStrings)
			{
				_configurations[css.Name] = new ConfigurationInfo(css);

				if (DefaultConfiguration == null && !IsMachineConfig(css))
				{
					DefaultConfiguration = css.Name;
				}
			}
		}

		static int _isInitialized;

		static void InitConfig()
		{
			if (Interlocked.Exchange(ref _isInitialized, 1) == 0)
				InitConnectionStrings();
		}

		static readonly ConcurrentDictionary<string,IDataProvider> _dataProviders =
			new ConcurrentDictionary<string,IDataProvider>();

		public static void AddDataProvider([JetBrains.Annotations.NotNull] string providerName, [JetBrains.Annotations.NotNull] IDataProvider dataProvider)
		{
			if (providerName == null) throw new ArgumentNullException("providerName");
			if (dataProvider == null) throw new ArgumentNullException("dataProvider");

			if (string.IsNullOrEmpty(dataProvider.Name))
				throw new ArgumentException("dataProvider.Name cannot be empty.", "dataProvider");

			_dataProviders[providerName] = dataProvider;
		}

		public static void AddDataProvider([JetBrains.Annotations.NotNull] IDataProvider dataProvider)
		{
			if (dataProvider == null) throw new ArgumentNullException("dataProvider");

			AddDataProvider(dataProvider.Name, dataProvider);
		}

		public static IDataProvider GetDataProvider([JetBrains.Annotations.NotNull] string configurationString)
		{
			InitConfig();

			return GetConfigurationInfo(configurationString).DataProvider;
		}

		class ConfigurationInfo
		{
			public ConfigurationInfo(string connectionString, IDataProvider dataProvider)
			{
				ConnectionString = connectionString;
				DataProvider     = dataProvider;
			}

			public ConfigurationInfo(ConnectionStringSettings connectionStringSettings)
			{
				ConnectionString = connectionStringSettings.ConnectionString;

				_connectionStringSettings = connectionStringSettings;
			}

			public  string ConnectionString;

			private readonly ConnectionStringSettings _connectionStringSettings;

			private IDataProvider _dataProvider;
			public  IDataProvider  DataProvider
			{
				get { return _dataProvider ?? (_dataProvider = GetDataProvider(_connectionStringSettings)); }
				set { _dataProvider = value; }
			}

			static IDataProvider GetDataProvider(ConnectionStringSettings css)
			{
				var configuration = css.Name;
				var providerName  = css.ProviderName;
				var dataProvider  = _providerDetectors.Select(d => d(css)).FirstOrDefault(dp => dp != null);

				if (dataProvider == null)
				{
					var defaultDataProvider = DefaultDataProvider != null ? _dataProviders[DefaultDataProvider] : null;

					if (string.IsNullOrEmpty(providerName))
						dataProvider = FindProvider(configuration, _dataProviders, defaultDataProvider);
					else if (_dataProviders.ContainsKey(providerName))
						dataProvider = _dataProviders[providerName];
					else if (_dataProviders.ContainsKey(configuration))
						dataProvider = _dataProviders[configuration];
					else
					{
						var providers = _dataProviders.Where(dp => dp.Value.ConnectionNamespace == providerName).ToList();

						switch (providers.Count)
						{
							case 0  : dataProvider = defaultDataProvider;                                        break;
							case 1  : dataProvider = providers[0].Value;                                         break;
							default : dataProvider = FindProvider(configuration, providers, providers[0].Value); break;
						}
					}
				}

				if (dataProvider != null && DefaultConfiguration == null && !IsMachineConfig(css))
				{
					DefaultConfiguration = css.Name;
				}

				return dataProvider;
			}
		}

		static ConfigurationInfo GetConfigurationInfo(string configurationString)
		{
			ConfigurationInfo ci;

			if (_configurations.TryGetValue(configurationString ?? DefaultConfiguration, out ci))
				return ci;

			throw new LinqToDBException("Configuration '{0}' is not defined.".Args(configurationString));
		}

		public static void SetConnectionStrings(System.Configuration.Configuration config)
		{
			foreach (ConnectionStringSettings css in config.ConnectionStrings.ConnectionStrings)
			{
				_configurations[css.Name] = new ConfigurationInfo(css);

				if (DefaultConfiguration == null && !IsMachineConfig(css))
				{
					DefaultConfiguration = css.Name;
				}
			}
		}

		static readonly ConcurrentDictionary<string,ConfigurationInfo> _configurations =
			new ConcurrentDictionary<string, ConfigurationInfo>();

		public static void AddConfiguration(
			[JetBrains.Annotations.NotNull] string configuration,
			[JetBrains.Annotations.NotNull] string connectionString,
			IDataProvider dataProvider = null)
		{
			if (configuration    == null) throw new ArgumentNullException("configuration");
			if (connectionString == null) throw new ArgumentNullException("connectionString");

			_configurations[configuration] = new ConfigurationInfo(
				connectionString,
				dataProvider ?? FindProvider(configuration, _dataProviders, _dataProviders[DefaultDataProvider]));
		}

		public static void SetConnectionString(
			[JetBrains.Annotations.NotNull] string configuration,
			[JetBrains.Annotations.NotNull] string connectionString)
		{
			if (configuration    == null) throw new ArgumentNullException("configuration");
			if (connectionString == null) throw new ArgumentNullException("connectionString");

			InitConfig();

			_configurations[configuration].ConnectionString = connectionString;
		}

		[Pure]
		public static string GetConnectionString(string configurationString)
		{
			InitConfig();

			ConfigurationInfo ci;

			if (_configurations.TryGetValue(configurationString, out ci))
				return ci.ConnectionString;

			throw new LinqToDBException("Configuration '{0}' is not defined.".Args(configurationString));
		}

		#endregion

		#region Connection

		bool          _closeConnection;
		bool          _closeTransaction;
		IDbConnection _connection;

		public IDbConnection Connection
		{
			get
			{
				if (_connection == null)
					_connection = DataProvider.CreateConnection(ConnectionString);

				if (_connection.State == ConnectionState.Closed)
				{
					_connection.Open();
					_closeConnection = true;
				}

				return _connection;
			}
		}

		public event EventHandler OnClosing;
		public event EventHandler OnClosed;

		public virtual void Close()
		{
			if (OnClosing != null)
				OnClosing(this, EventArgs.Empty);

			DisposeCommand();

            //if (Transaction != null && _closeTransaction)
            //{
            //    Transaction.Dispose();
            //    Transaction = null;
            //}

			if (_connection != null && _closeConnection)
			{
				_connection.Dispose();
				_connection = null;
			}

			if (OnClosed != null)
				OnClosed(this, EventArgs.Empty);
		}

		#endregion

	    internal string sqlString = string.Empty;
		#region Command

		public string LastQuery;

		internal void InitCommand(CommandType commandType, string sql, DataParameter[] parameters, List<string> queryHints)
		{
            
            if (queryHints != null && queryHints.Count > 0)
            {
                var sqlProvider = DataProvider.CreateSqlBuilder();
                sql = sqlProvider.ApplyQueryHints(sql, queryHints);
                queryHints.Clear();
            }
            sqlString = sql;
		    LastQuery = sql;
		    //DataProvider.InitCommand(this, commandType, sql, parameters);
		    //LastQuery = Command.CommandText;


		}

		private int? _commandTimeout;
        /// <summary>
        /// 设置0代表采用默认的
        /// </summary>
		public  int   CommandTimeout
		{
			get { return _commandTimeout ?? 0; }
			set { _commandTimeout = value;     }
		}

		private IDbCommand _command;
		public  IDbCommand  Command
		{
			get { return _command ?? (_command = CreateCommand()); }
			set { _command = value; }
		}

        internal Dictionary<string, CustomerParam> Params = new Dictionary<string, CustomerParam>();

        public IDbCommand CreateCommand()
		{

			var command = Connection.CreateCommand();

            if (_commandTimeout.HasValue)
				command.CommandTimeout = _commandTimeout.Value;

            //if (Transaction != null)
            //    command.Transaction = Transaction;

			return command;
		}

		public void DisposeCommand()
		{
			if (_command != null)
			{
				DataProvider.DisposeCommand(this);
				_command = null;
			}
		}

		internal int ExecuteNonQuery()
		{

            var dic = new Dictionary<string, object>();
		    if (this.CommandTimeout > 0)
		    {
                dic.Add("TIMEOUT", this.CommandTimeout);
		    }
            var result = CustomerExecuteNonQuery(ConnectionString, sqlString, Params, dic);
            if (OnCustomerTraceConnection!=null)
		    {
		        OnCustomerTraceConnection(new CustomerTraceInfo
		        {
                    CustomerParams = Params,
                    SqlText = sqlString
		        });
		    }
            this.Dispose();
            return result;
            //return baseDao.ExecNonQuery(sqlString);
            if (TraceSwitch.Level == TraceLevel.Off)
				return Command.ExecuteNonQuery();

			if (OnTraceConnection == null)
				return Command.ExecuteNonQuery();

			if (TraceSwitch.TraceInfo)
			{
				OnTraceConnection(new TraceInfo
				{
					BeforeExecute  = true,
					TraceLevel     = TraceLevel.Info,
					DataConnection = this,
					Command        = Command,
				});
			}

			var now = DateTime.Now;

			try
			{
				var ret = Command.ExecuteNonQuery();

				if (TraceSwitch.TraceInfo)
				{
					OnTraceConnection(new TraceInfo
					{
						TraceLevel      = TraceLevel.Info,
						DataConnection  = this,
						Command         = Command,
						ExecutionTime   = DateTime.Now - now,
						RecordsAffected = ret,
					});
				}

				return ret;
			}
			catch (Exception ex)
			{
				if (TraceSwitch.TraceError)
				{
					OnTraceConnection(new TraceInfo
					{
						TraceLevel     = TraceLevel.Error,
						DataConnection = this,
						Command        = Command,
						ExecutionTime  = DateTime.Now - now,
						Exception      = ex,
					});
				}

				throw;
			}
		}

		object ExecuteScalar()
		{
            var dic = new Dictionary<string, object>();
            if (this.CommandTimeout > 0)
            {
                dic.Add("TIMEOUT", this.CommandTimeout);
            }
            var result = CustomerExecuteScalar(ConnectionString, sqlString, Params, dic);
            if (OnCustomerTraceConnection != null)
            {
                OnCustomerTraceConnection(new CustomerTraceInfo
                {
                    CustomerParams = Params,
                    SqlText = sqlString
                });
            }
            this.Dispose();
            return result;
            //return baseDao.ExecScalar(sqlString);
            if (TraceSwitch.Level == TraceLevel.Off)
				return Command.ExecuteScalar();

			if (OnTraceConnection == null)
				return Command.ExecuteScalar();

			if (TraceSwitch.TraceInfo)
			{
				OnTraceConnection(new TraceInfo
				{
					BeforeExecute  = true,
					TraceLevel     = TraceLevel.Info,
					DataConnection = this,
					Command        = Command,
				});
			}

			var now = DateTime.Now;

			try
			{
				var ret = Command.ExecuteScalar();

				if (TraceSwitch.TraceInfo)
				{
					OnTraceConnection(new TraceInfo
					{
						TraceLevel     = TraceLevel.Info,
						DataConnection = this,
						Command        = Command,
						ExecutionTime  = DateTime.Now - now,
					});
				}

				return ret;
			}
			catch (Exception ex)
			{
				if (TraceSwitch.TraceError)
				{
					OnTraceConnection(new TraceInfo
					{
						TraceLevel     = TraceLevel.Error,
						DataConnection = this,
						Command        = Command,
						ExecutionTime  = DateTime.Now - now,
						Exception      = ex,
					});
				}

				throw;
			}
		}

		internal IDataReader ExecuteReader()
		{
            var dic = new Dictionary<string, object>();
            if (this.CommandTimeout > 0)
            {
                dic.Add("TIMEOUT", this.CommandTimeout);
            }
            var result =  CustomerExecuteQuery(ConnectionString,sqlString, Params,dic);
            if (OnCustomerTraceConnection != null)
            {
                OnCustomerTraceConnection(new CustomerTraceInfo
                {
                    CustomerParams = Params,
                    SqlText = sqlString
                });
            }
             this.Dispose();
		    return result;
            OnTraceConnection(new TraceInfo());
            //return baseDao.SelectDataReader(sqlString);
			return ExecuteReader(CommandBehavior.Default);
		}

		internal IDataReader ExecuteReader(CommandBehavior commandBehavior)
		{
            var dic = new Dictionary<string, object>();
            if (this.CommandTimeout > 0)
            {
                dic.Add("TIMEOUT", this.CommandTimeout);
            }
            var result = CustomerExecuteQuery(ConnectionString, sqlString, Params,dic);
            if (OnCustomerTraceConnection != null)
            {
                OnCustomerTraceConnection(new CustomerTraceInfo
                {
                    CustomerParams = Params,
                    SqlText = sqlString
                });
            }
            this.Dispose();
            return result;
            OnTraceConnection(new TraceInfo());
           // return baseDao.SelectDataReader(sqlString);
            if (TraceSwitch.Level == TraceLevel.Off)
				return Command.ExecuteReader(commandBehavior);

			if (OnTraceConnection == null)
				return Command.ExecuteReader(commandBehavior);

			if (TraceSwitch.TraceInfo)
			{
				OnTraceConnection(new TraceInfo
				{
					BeforeExecute  = true,
					TraceLevel     = TraceLevel.Info,
					DataConnection = this,
					Command        = Command,
				});
			}

			var now = DateTime.Now;

			try
			{
				var ret = Command.ExecuteReader(commandBehavior);

				if (TraceSwitch.TraceInfo)
				{
					OnTraceConnection(new TraceInfo
					{
						TraceLevel     = TraceLevel.Info,
						DataConnection = this,
						Command        = Command,
						ExecutionTime  = DateTime.Now - now,
					});
				}

				return ret;
			}
			catch (Exception ex)
			{
				if (TraceSwitch.TraceError)
				{
					OnTraceConnection(new TraceInfo
					{
						TraceLevel     = TraceLevel.Error,
						DataConnection = this,
						Command        = Command,
						ExecutionTime  = DateTime.Now - now,
						Exception      = ex,
					});
				}

				throw;
			}
		}

        internal DataTable ExecuteDataTable(CommandBehavior commandBehavior)
        {
            var dic = new Dictionary<string, object>();
            if (this.CommandTimeout > 0)
            {
                dic.Add("TIMEOUT", this.CommandTimeout);
            }
            var result = CustomerExecuteQueryTable(ConnectionString, sqlString, Params, dic);
            if (OnCustomerTraceConnection != null)
            {
                OnCustomerTraceConnection(new CustomerTraceInfo
                {
                    CustomerParams = Params,
                    SqlText = sqlString
                });
            }
            this.Dispose();
            return result;
        }
		public static void ClearObjectReaderCache()
		{
			CommandInfo.ClearObjectReaderCache();
		}

		#endregion

		#region Transaction

        //public IDbTransaction Transaction { get; private set; }
		
        //public virtual DataConnectionTransaction BeginTransaction()
        //{
        //    // If transaction is open, we dispose it, it will rollback all changes.
        //    //
        //    if (Transaction != null)
        //        Transaction.Dispose();

        //    // Create new transaction object.
        //    //
        //    Transaction = Connection.BeginTransaction();

        //    _closeTransaction = true;

        //    // If the active command exists.
        //    //
        //    if (_command != null)
        //        _command.Transaction = Transaction;

        //    return new DataConnectionTransaction(this);
        //}

        //public virtual DataConnectionTransaction BeginTransaction(IsolationLevel isolationLevel)
        //{
        //    // If transaction is open, we dispose it, it will rollback all changes.
        //    //
        //    if (Transaction != null)
        //        Transaction.Dispose();

        //    // Create new transaction object.
        //    //
        //    Transaction = Connection.BeginTransaction(isolationLevel);

        //    _closeTransaction = true;

        //    // If the active command exists.
        //    //
        //    if (_command != null)
        //        _command.Transaction = Transaction;

        //    return new DataConnectionTransaction(this);
        //}

        //public virtual void CommitTransaction()
        //{
        //    if (Transaction != null)
        //    {
        //        Transaction.Commit();

        //        if (_closeTransaction)
        //        {
        //            Transaction.Dispose();
        //            Transaction = null;
        //        }
        //    }
        //}

        //public virtual void RollbackTransaction()
        //{
        //    if (Transaction != null)
        //    {
        //        Transaction.Rollback();

        //        if (_closeTransaction)
        //        {
        //            Transaction.Dispose();
        //            Transaction = null;
        //        }
        //    }
        //}

		#endregion



		#region MappingSchema

		private MappingSchema _mappingSchema;

		public  MappingSchema  MappingSchema
		{
			get { return _mappingSchema; }
		}

		public bool InlineParameters { get; set; }

		private List<string> _queryHints;
		public  List<string>  QueryHints
		{
			get { return _queryHints ?? (_queryHints = new List<string>()); }
		}

		private List<string> _nextQueryHints;
		public  List<string>  NextQueryHints
		{
			get { return _nextQueryHints ?? (_nextQueryHints = new List<string>()); }
		}

		public DataConnection AddMappingSchema(MappingSchema mappingSchema)
		{
			_mappingSchema = new MappingSchema(mappingSchema, _mappingSchema);
			_id            = null;

			return this;
		}

		#endregion

		#region ICloneable Members

		DataConnection(string configurationString, IDataProvider dataProvider, string connectionString, IDbConnection connection, MappingSchema mappingSchema)
		{
			ConfigurationString = configurationString;
			DataProvider        = dataProvider;
			ConnectionString    = connectionString;
			_connection         = connection;
			_mappingSchema      = mappingSchema;
			_closeConnection    = true;
		}

		public object Clone()
		{
			var connection =
				_connection == null       ? null :
				_connection is ICloneable ? (IDbConnection)((ICloneable)_connection).Clone() :
				                            DataProvider.CreateConnection(ConnectionString);

			return new DataConnection(ConfigurationString, DataProvider, ConnectionString, connection, MappingSchema);
		}
		
		#endregion

		#region System.IDisposable Members

		public void Dispose()
		{
            this.Params = new Dictionary<string, CustomerParam>();
		    this.sqlString = string.Empty;
		    this.LastQuery = string.Empty;
			Close();
		}

		#endregion
	}
}