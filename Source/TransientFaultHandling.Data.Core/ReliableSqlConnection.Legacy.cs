﻿namespace Microsoft.Practices.EnterpriseLibrary.TransientFaultHandling
{
    using System;
    using System.Data;
    using System.Data.SqlClient;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Xml;

    /// <summary>
    /// Provides a reliable way of opening connections to, and executing commands against, the SQL Database 
    /// databases taking potential network unreliability and connection retry requirements into account.
    /// </summary>
    [Obsolete("Use Microsoft.Practices.EnterpriseLibrary.TransientFaultHandling.ReliableSqlConnection with Microsoft.Data.SqlClient.")]
    public sealed class ReliableSqlConnectionLegacy : IDbConnection, ICloneable
    {
        private readonly RetryPolicy connectionStringFailoverPolicy;

        private string connectionString;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReliableSqlConnection"/> class with the specified connection string. Uses the default
        /// retry policy for connections and commands unless retry settings are provided in the connection string.
        /// </summary>
        /// <param name="connectionString">The connection string used to open the SQL Database.</param>
        public ReliableSqlConnectionLegacy(string connectionString)
            : this(connectionString, RetryManager.Instance.GetDefaultSqlConnectionRetryPolicy())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ReliableSqlConnection"/> class with the specified connection string
        /// and a policy that defines whether to retry a request if a connection or command
        /// fails.
        /// </summary>
        /// <param name="connectionString">The connection string used to open the SQL Database.</param>
        /// <param name="retryPolicy">The retry policy that defines whether to retry a request if a connection or command fails.</param>
        public ReliableSqlConnectionLegacy(string connectionString, RetryPolicy retryPolicy)
            : this(connectionString, retryPolicy, RetryManager.Instance.GetDefaultSqlCommandRetryPolicy() ?? retryPolicy)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ReliableSqlConnection"/> class with the specified connection string
        /// and a policy that defines whether to retry a request if a connection or command
        /// fails.
        /// </summary>
        /// <param name="connectionString">The connection string used to open the SQL Database.</param>
        /// <param name="connectionRetryPolicy">The retry policy that defines whether to retry a request if a connection fails to be established.</param>
        /// <param name="commandRetryPolicy">The retry policy that defines whether to retry a request if a command fails to be executed.</param>
        public ReliableSqlConnectionLegacy(string connectionString, RetryPolicy connectionRetryPolicy, RetryPolicy commandRetryPolicy)
        {
            this.connectionString = connectionString;
            this.Current = new SqlConnection(connectionString);
            this.ConnectionRetryPolicy = connectionRetryPolicy;
            this.CommandRetryPolicy = commandRetryPolicy;

            // Configure a special retry policy that enables detecting network connectivity errors to be able to determine whether we need to failover
            // to the original connection string containing the DNS name of the SQL Database server.
            this.connectionStringFailoverPolicy = new RetryPolicy<NetworkConnectivityErrorDetectionStrategy>(1, TimeSpan.FromMilliseconds(1));
        }

        #region Public properties
        /// <summary>
        /// Gets or sets the connection string for opening a connection to the SQL Database.
        /// </summary>
        public string ConnectionString
        {
            get => this.connectionString;

            set
            {
                this.connectionString = value;
                this.Current.ConnectionString = value;
            }
        }

        /// <summary>
        /// Gets the policy that determines whether to retry a connection request, based on how many 
        /// times the request has been made and the reason for the last failure. 
        /// </summary>
        public RetryPolicy ConnectionRetryPolicy { get; }

        /// <summary>
        /// Gets the policy that determines whether to retry a command, based on how many 
        /// times the request has been made and the reason for the last failure. 
        /// </summary>
        public RetryPolicy CommandRetryPolicy { get; }

        /// <summary>
        /// Gets an instance of the SqlConnection object that represents the connection to a SQL Database instance.
        /// </summary>
        public SqlConnection Current { get; }

        /// <summary>
        /// Gets the CONTEXT_INFO value that was set for the current session. This value can be used to trace query execution problems. 
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "As designed")]
        public Guid SessionTracingId
        {
            get
            {
                try
                {
                    using IDbCommand query = SqlCommandFactory.CreateGetContextInfoCommand(this.Current);
                    // Execute the query in retry-aware fashion, retry if necessary.
                    return this.ExecuteCommand<Guid>(query);
                }
                catch
                {
                    // Any failure will result in returning a default GUID value. This is by design.
                    return Guid.Empty;
                }
            }
        }

        /// <summary>
        /// Gets a value that specifies the time to wait while trying to establish a connection before terminating
        /// the attempt and generating an error.
        /// </summary>
        public int ConnectionTimeout => this.Current.ConnectionTimeout;

        /// <summary>
        /// Gets the name of the current database or the database to be used after a
        /// connection is opened.
        /// </summary>
        public string Database => this.Current.Database;

        /// <summary>
        /// Gets the current state of the connection.
        /// </summary>
        public ConnectionState State => this.Current.State;

        #endregion

        #region Public methods
        /// <summary>
        /// Opens a database connection with the settings specified by the ConnectionString and ConnectionRetryPolicy properties.
        /// </summary>
        /// <returns>An object that represents the open connection.</returns>
        public SqlConnection Open() => this.Open(this.ConnectionRetryPolicy);

        /// <summary>
        /// Opens a database connection with the settings specified by the connection string and the specified retry policy.
        /// </summary>
        /// <param name="retryPolicy">The retry policy that defines whether to retry a request if the connection fails to open.</param>
        /// <returns>An object that represents the open connection.</returns>
        public SqlConnection Open(RetryPolicy retryPolicy)
        {
            // Check if retry policy was specified, if not, disable retries by executing the Open method using RetryPolicy.NoRetry.
            (retryPolicy ?? RetryPolicy.NoRetry).ExecuteAction(() =>
                this.connectionStringFailoverPolicy.ExecuteAction(() =>
                {
                    if (this.Current.State != ConnectionState.Open)
                    {
                        this.Current.Open();
                    }
                }));

            return this.Current;
        }

        /// <summary>
        /// Executes a SQL command and returns a result that is defined by the specified type <typeparamref name="T"/>. This method uses the retry policy specified when 
        /// instantiating the SqlAzureConnection class (or the default retry policy if no policy was set at construction time).
        /// </summary>
        /// <typeparam name="T">IDataReader, XmlReader, or any other .NET Framework type that defines the type of result to be returned.</typeparam>
        /// <param name="command">The SQL command to be executed.</param>
        /// <returns>An instance of an IDataReader, XmlReader, or any other .NET Framework object that contains the result.</returns>
        public T ExecuteCommand<T>(IDbCommand command) => this.ExecuteCommand<T>(command, this.CommandRetryPolicy, CommandBehavior.Default);

        /// <summary>
        /// Executes a SQL command and returns a result that is defined by the specified type <typeparamref name="T"/>. This method uses the retry policy specified when 
        /// instantiating the SqlAzureConnection class (or the default retry policy if no policy was set at construction time).
        /// </summary>
        /// <typeparam name="T">IDataReader, XmlReader, or any other .NET Framework type that defines the type of result to be returned.</typeparam>
        /// <param name="command">The SQL command to be executed.</param>
        /// <param name="behavior">A description of the results of the query and its effect on the database.</param>
        /// <returns>An instance of an IDataReader, XmlReader, or any other .NET Frameork object that contains the result.</returns>
        public T ExecuteCommand<T>(IDbCommand command, CommandBehavior behavior) => this.ExecuteCommand<T>(command, this.CommandRetryPolicy, behavior);

        /// <summary>
        /// Executes a SQL command by using the specified retry policy, and returns a result that is defined by the specified type <typeparamref name="T"/>
        /// </summary>
        /// <typeparam name="T">IDataReader, XmlReader, or any other .NET Framework type that defines the type of result to be returned.</typeparam>
        /// <param name="command">The SQL command to be executed.</param>
        /// <param name="retryPolicy">The retry policy that defines whether to retry a command if a connection fails while executing the command.</param>
        /// <returns>An instance of an IDataReader, XmlReader, or any other .NET Frameork object that contains the result.</returns>
        public T ExecuteCommand<T>(IDbCommand command, RetryPolicy retryPolicy) => this.ExecuteCommand<T>(command, retryPolicy, CommandBehavior.Default);

        /// <summary>
        /// Executes a SQL command by using the specified retry policy, and returns a result that is defined by the specified type <typeparamref name="T"/>
        /// </summary>
        /// <typeparam name="T">IDataReader, XmlReader, or any other .NET Framework type that defines the type of result to be returned.</typeparam>
        /// <param name="command">The SQL command to be executed.</param>
        /// <param name="retryPolicy">The retry policy that defines whether to retry a command if a connection fails while executing the command.</param>
        /// <param name="behavior">A description of the results of the query and its effect on the database.</param>
        /// <returns>An instance of an IDataReader, XmlReader, or any other .NET Frameork object that contains the result.</returns>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by client code")]
        public T ExecuteCommand<T>(IDbCommand command, RetryPolicy retryPolicy, CommandBehavior behavior)
        {
            T actionResult = default;

            Type resultType = typeof(T);

            bool hasOpenedConnection = false;

            bool closeOpenedConnectionOnSuccess = false;

            try
            {
                (retryPolicy ?? RetryPolicy.NoRetry).ExecuteAction(() =>
                {
                    actionResult = this.connectionStringFailoverPolicy.ExecuteAction(() =>
                    {
                        // Make sure the command has been associated with a valid connection. If not, associate it with an opened SQL connection.
                        if (command.Connection is null)
                        {
                            // Open a new connection and assign it to the command object.
                            command.Connection = this.Open();
                            hasOpenedConnection = true;
                        }

                        // Verify whether or not the connection is valid and is open. This code may be retried therefore
                        // it is important to ensure that a connection is re-established should it have previously failed.
                        if (command.Connection.State != ConnectionState.Open)
                        {
                            command.Connection.Open();
                            hasOpenedConnection = true;
                        }

                        if (typeof(IDataReader).IsAssignableFrom(resultType))
                        {
                            closeOpenedConnectionOnSuccess = false;

                            return (T)command.ExecuteReader(behavior);
                        }

                        if (resultType == typeof(XmlReader))
                        {
                            if (command is SqlCommand)
                            {
                                object result;
                                XmlReader xmlReader = (command as SqlCommand).ExecuteXmlReader();

                                closeOpenedConnectionOnSuccess = false;

                                if ((behavior & CommandBehavior.CloseConnection) == CommandBehavior.CloseConnection)
                                {
                                    // Implicit conversion from XmlReader to <T> via an intermediary object.
                                    result = new SqlXmlReader(command.Connection, xmlReader);
                                }
                                else
                                {
                                    // Implicit conversion from XmlReader to <T> via an intermediary object.
                                    result = xmlReader;
                                }

                                return (T)result;
                            }

                            throw new NotSupportedException();
                        }

                        if (resultType == typeof(NonQueryResult))
                        {
                            NonQueryResult result = new() { RecordsAffected = command.ExecuteNonQuery() };

                            closeOpenedConnectionOnSuccess = true;

                            return (T)Convert.ChangeType(result, resultType, CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            object result = command.ExecuteScalar();

                            closeOpenedConnectionOnSuccess = true;

                            if (result is not null)
                            {
                                return (T)Convert.ChangeType(result, resultType, CultureInfo.InvariantCulture);
                            }

                            return default;
                        }
                    });
                });

                if (hasOpenedConnection && closeOpenedConnectionOnSuccess &&
                   command.Connection is not null && command.Connection.State == ConnectionState.Open)
                {
                    command.Connection.Close();
                }
            }
            catch (Exception)
            {
                if (hasOpenedConnection &&
                   command.Connection is not null && command.Connection.State == ConnectionState.Open)
                {
                    command.Connection.Close();
                }

                throw;
            }

            return actionResult;
        }

        /// <summary>
        /// Executes a SQL command and returns the number of rows affected.
        /// </summary>
        /// <param name="command">The SQL command to be executed.</param>
        /// <returns>The number of rows affected.</returns>
        public int ExecuteCommand(IDbCommand command) => this.ExecuteCommand(command, this.CommandRetryPolicy);

        /// <summary>
        /// Executes a SQL command and returns the number of rows affected.
        /// </summary>
        /// <param name="command">The SQL command to be executed.</param>
        /// <param name="retryPolicy">The retry policy that defines whether to retry a command if a connection fails while executing the command.</param>
        /// <returns>The number of rows affected.</returns>
        public int ExecuteCommand(IDbCommand command, RetryPolicy retryPolicy)
        {
            NonQueryResult result = this.ExecuteCommand<NonQueryResult>(command, retryPolicy);

            return result.RecordsAffected;
        }
        #endregion

        #region IDbConnection implementation
        /// <summary>
        /// Begins a database transaction with the specified System.Data.IsolationLevel value.
        /// </summary>
        /// <param name="il">One of the enumeration values that specifies the isolation level for the transaction.</param>
        /// <returns>An object that represents the new transaction.</returns>
        public IDbTransaction BeginTransaction(IsolationLevel il) => this.Current.BeginTransaction(il);

        /// <summary>
        /// Begins a database transaction.
        /// </summary>
        /// <returns>An object that represents the new transaction.</returns>
        public IDbTransaction BeginTransaction() => this.Current.BeginTransaction();

        /// <summary>
        /// Changes the current database for an open Connection object.
        /// </summary>
        /// <param name="databaseName">The name of the database to use in place of the current database.</param>
        public void ChangeDatabase(string databaseName) => this.Current.ChangeDatabase(databaseName);

        /// <summary>
        /// Opens a database connection with the settings specified by the ConnectionString
        /// property of the provider-specific Connection object.
        /// </summary>
        void IDbConnection.Open() => this.Open();

        /// <summary>
        /// Closes the connection to the database.
        /// </summary>
        public void Close() => this.Current.Close();

        /// <summary>
        /// Creates and returns a SqlCommand object that is associated with the underlying SqlConnection.
        /// </summary>
        /// <returns>A System.Data.SqlClient.SqlCommand object that is associated with the underlying connection.</returns>
        public SqlCommand CreateCommand() => this.Current.CreateCommand();

        /// <summary>
        /// Creates and returns an object that implements the IDbCommand interface that is associated 
        /// with the underlying SqlConnection.
        /// </summary>
        /// <returns>A System.Data.SqlClient.SqlCommand object that is associated with the underlying connection.</returns>
        IDbCommand IDbConnection.CreateCommand() => this.Current.CreateCommand();

        #endregion

        #region ICloneable implementation
        /// <summary>
        /// Creates a new connection that is a copy of the current instance, including the connection
        /// string, connection retry policy, and command retry policy.
        /// </summary>
        /// <returns>A new object that is a copy of this instance.</returns>
        object ICloneable.Clone() => 
            new ReliableSqlConnection(this.ConnectionString, this.ConnectionRetryPolicy, this.CommandRetryPolicy);

        #endregion

        #region IDisposable implementation
        /// <summary>
        /// Performs application-defined tasks that are associated with freeing, releasing, or
        /// resetting managed and unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or
        /// resetting managed and unmanaged resources.
        /// </summary>
        /// <param name="disposing">A flag indicating that managed resources must be released.</param>
        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.Current.State == ConnectionState.Open)
                {
                    this.Current.Close();
                }

                this.Current.Dispose();
            }
        }
        #endregion

        #region Private helper classes
        /// <summary>
        /// This helpers class is intended to be used exclusively for fetching the number of affected records when executing a command by using ExecuteNonQuery.
        /// </summary>
        private sealed class NonQueryResult
        {
            public int RecordsAffected { get; set; }
        }
        #endregion

        /// <summary>
        /// Implements a strategy that detects network connectivity errors such as "host not found".
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses", Justification = "Instantiated through generics")]
        private sealed class NetworkConnectivityErrorDetectionStrategy : ITransientErrorDetectionStrategy
        {
            public bool IsTransient(Exception ex)
            {
                if (ex is not null and SqlException sqlException)
                {
                    switch (sqlException.Number)
                    {
                        // SQL Error Code: 11001
                        // A network-related or instance-specific error occurred while establishing a connection to SQL Server. 
                        // The server was not found or was not accessible. Verify that the instance name is correct and that SQL 
                        // Server is configured to allow remote connections. (provider: TCP Provider, error: 0 - No such host is known.)
                        case 11001:
                            return true;
                    }
                }

                return false;
            }
        }
    }
}