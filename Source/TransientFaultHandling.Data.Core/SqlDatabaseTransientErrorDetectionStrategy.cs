﻿namespace Microsoft.Practices.EnterpriseLibrary.TransientFaultHandling
{
    using System;
    using System.Data;
    using Microsoft.Data.SqlClient;
    using Microsoft.Practices.EnterpriseLibrary.TransientFaultHandling.Data;
    using Microsoft.Practices.EnterpriseLibrary.TransientFaultHandling.Properties;

    /// <summary>
    /// Provides the transient error detection logic for transient faults that are specific to SQL Database.
    /// </summary>
    public sealed class SqlDatabaseTransientErrorDetectionStrategy : ITransientErrorDetectionStrategy
    {
        #region ProcessNetLibErrorCode enumeration

        /// <summary>
        /// Error codes reported by the DBNETLIB module.
        /// </summary>
        private enum ProcessNetLibErrorCode
        {
            ZeroBytes = -3,

            Timeout = -2, /* Timeout expired. The timeout period elapsed prior to completion of the operation or the server is not responding. */

            Unknown = -1,

            InsufficientMemory = 1,

            AccessDenied = 2,

            ConnectionBusy = 3,

            ConnectionBroken = 4,

            ConnectionLimit = 5,

            ServerNotFound = 6,

            NetworkNotFound = 7,

            InsufficientResources = 8,

            NetworkBusy = 9,

            NetworkAccessDenied = 10,

            GeneralError = 11,

            IncorrectMode = 12,

            NameNotFound = 13,

            InvalidConnection = 14,

            ReadWriteError = 15,

            TooManyHandles = 16,

            ServerError = 17,

            SSLError = 18,

            EncryptionError = 19,

            EncryptionNotSupported = 20
        }

        #endregion
        // this is a specific error class we need to look for see http://technet.microsoft.com/en-us/library/aa937483(v=SQL.80).aspx

        #region ITransientErrorDetectionStrategy implementation

        /// <summary>
        /// Determines whether the specified exception represents a transient failure that can be compensated by a retry.
        /// </summary>
        /// <param name="ex">The exception object to be verified.</param>
        /// <returns>true if the specified exception is considered as transient; otherwise, false.</returns>
        public bool IsTransient(Exception ex)
        {
            if (ex is not null)
            {
                if (ex is SqlException sqlException)
                {
                    // Enumerate through all errors found in the exception.
                    foreach (SqlError err in sqlException.Errors)
                    {
                        switch (err.Number)
                        {
                            // SQL Error Code: 40501
                            // The service is currently busy. Retry the request after 10 seconds. Code: (reason code to be decoded).
                            case ThrottlingCondition.ThrottlingErrorNumber:
                                // Decode the reason code from the error message to determine the grounds for throttling.
                                ThrottlingCondition condition = ThrottlingCondition.FromError(err);

                                // Attach the decoded values as additional attributes to the original SQL exception.
                                sqlException.Data[condition.ThrottlingMode.GetType().Name] =
                                    condition.ThrottlingMode.ToString();
                                sqlException.Data[condition.GetType().Name] = condition;

                                return true;
                            case 0:
                                if ((err.Class == 20 || err.Class == 11) && err.State == 0 && err.Server is not null && ex.InnerException is null)
                                {
                                    if (string.Equals(err.Message, Resources.SQL_SevereError, StringComparison.CurrentCultureIgnoreCase))
                                    {
                                        return true;
                                    }
                                }
                                return false;
                            // SQL Error Code: 4060
                            // Cannot open database "%.*ls" requested by the login. The login failed.
                            case 4060:
                            // SQL Error Code: 10928
                            // Resource ID: %d. The %s limit for the database is %d and has been reached.
                            case 10928:
                            // SQL Error Code: 10929
                            // Resource ID: %d. The %s minimum guarantee is %d, maximum limit is %d and the current usage for the database is %d. 
                            // However, the server is currently too busy to support requests greater than %d for this database.
                            case 10929:
                            // SQL Error Code: 10053
                            // A transport-level error has occurred when receiving results from the server.
                            // An established connection was aborted by the software in your host machine.
                            case 10053:
                            // SQL Error Code: 10054
                            // A transport-level error has occurred when sending the request to the server. 
                            // (provider: TCP Provider, error: 0 - An existing connection was forcibly closed by the remote host.)
                            case 10054:
                            // SQL Error Code: 10060
                            // A network-related or instance-specific error occurred while establishing a connection to SQL Server. 
                            // The server was not found or was not accessible. Verify that the instance name is correct and that SQL Server 
                            // is configured to allow remote connections. (provider: TCP Provider, error: 0 - A connection attempt failed 
                            // because the connected party did not properly respond after a period of time, or established connection failed 
                            // because connected host has failed to respond.)"}
                            case 10060:
                            // SQL Error Code: 40197
                            // The service has encountered an error processing your request. Please try again.
                            case 40197:
                            // SQL Error Code: 40540
                            // The service has encountered an error processing your request. Please try again.
                            case 40540:
                            // SQL Error Code: 40613
                            // Database XXXX on server YYYY is not currently available. Please retry the connection later. If the problem persists, contact customer 
                            // support, and provide them the session tracing ID of ZZZZZ.
                            case 40613:
                            // SQL Error Code: 40143
                            // The service has encountered an error processing your request. Please try again.
                            case 40143:
                            // SQL Error Code: 233
                            // The client was unable to establish a connection because of an error during connection initialization process before login. 
                            // Possible causes include the following: the client tried to connect to an unsupported version of SQL Server; the server was too busy 
                            // to accept new connections; or there was a resource limitation (insufficient memory or maximum allowed connections) on the server. 
                            // (provider: TCP Provider, error: 0 - An existing connection was forcibly closed by the remote host.)
                            case 233:
                            // SQL Error Code: 64
                            // A connection was successfully established with the server, but then an error occurred during the login process. 
                            // (provider: TCP Provider, error: 0 - The specified network name is no longer available.) 
                            case 64:
                            // DBNETLIB Error Code: 20
                            // The instance of SQL Server you attempted to connect to does not support encryption.
                            case (int)ProcessNetLibErrorCode.EncryptionNotSupported:
                                return true;
                        }
                    }
                }
                else if (ex is TimeoutException)
                {
                    return true;
                }
                else
                {
                    if (ex is EntityException entityException)
                    {
                        return this.IsTransient(entityException.InnerException);
                    }
                }
            }

            return false;
        }

        #endregion
    }
}

namespace Microsoft.Practices.EnterpriseLibrary.WindowsAzure.TransientFaultHandling.SqlAzure
{
    using System;
    using Microsoft.Practices.EnterpriseLibrary.TransientFaultHandling;

    /// <summary>
    /// This class is obsolete. The non-obsolete alternative is <see cref="SqlDatabaseTransientErrorDetectionStrategy"/>.
    /// Provides the transient error detection logic for transient faults that are specific to SQL Database.
    /// </summary>
    [Obsolete("Use SqlDatabaseTransientErrorDetectionStrategy instead.", false)]
    public class SqlAzureTransientErrorDetectionStrategy : ITransientErrorDetectionStrategy
    {
        private readonly SqlDatabaseTransientErrorDetectionStrategy inner = new();

        /// <summary>
        /// Determines whether the specified exception represents a transient failure that can be compensated by a retry.
        /// </summary>
        /// <param name="ex">The exception object to be verified.</param>
        /// <returns>true if the specified exception is considered transient; otherwise, false.</returns>
        public bool IsTransient(Exception ex) => this.inner.IsTransient(ex);
    }
}

namespace System.Data
{
    using System.Runtime.Serialization;
    using Microsoft.Practices.EnterpriseLibrary.TransientFaultHandling.Properties;

    /// <summary>Represents Entity Framework-related errors that occur in the <see langword="EntityClient" /> namespace. The <see langword="EntityException" /> is the base class for all Entity Framework exceptions thrown by the <see langword="EntityClient" />.</summary>
    [Serializable]
    public class EntityException : DataException
    {
        /// <summary>Initializes a new instance of the <see cref="T:System.Data.EntityException" /> class.</summary>
        public EntityException()
          : base(Resources.EntityClient_ProviderGeneralError)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="T:System.Data.EntityException" /> class.</summary>
        /// <param name="message">The message that describes the error.</param>
        public EntityException(string message)
          : base(message)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="T:System.Data.EntityException" /> class.</summary>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        /// <param name="innerException">The exception that caused the current exception, or a <see langword="null" /> reference (<see langword="Nothing" /> in Visual Basic) if no inner exception is specified.</param>
        public EntityException(string message, Exception innerException)
          : base(message, innerException)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="T:System.Data.EntityException" /> class.</summary>
        /// <param name="info">The <see cref="T:System.Runtime.Serialization.SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="T:System.Runtime.Serialization.StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected EntityException(SerializationInfo info, StreamingContext context)
          : base(info, context)
        {
        }
    }
}