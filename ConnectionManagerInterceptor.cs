using System;
using System.Data;
using System.Reflection;
using EY.Platform.Operations;
using EY.Platform.ServiceBus.Interceptors;
using JetBrains.Annotations;

namespace EY.Platform.DAL.Interceptors
{
	public class ConnectionManagerInterceptor : IServiceBusInterceptor
	{
		#region [Private fields]
		private readonly IOperationProvider _operationProvider;
		private readonly IConnectionManager _connectionManager;
		private readonly IsolationLevel _defaultTransactionIsolationLevel;
		#endregion

		#region [Constructors]
		public ConnectionManagerInterceptor(
			[NotNull] IOperationProvider operationProvider, 
			[NotNull] IConnectionManager connectionManager, 
			IsolationLevel defaultTransactionIsolationLevel)
		{
			if (operationProvider == null)
			{
				throw new ArgumentNullException(nameof(operationProvider));
			}
			if (connectionManager == null)
			{
				throw new ArgumentNullException(nameof(connectionManager));
			}

			_operationProvider = operationProvider;
			_connectionManager = connectionManager;
			_defaultTransactionIsolationLevel = defaultTransactionIsolationLevel;
		}
		#endregion

		#region [Implements of IServiceBusExecutionInterceptor]
		public Result<TResultValue> Intercept<TContract, TResultValue>(
			Session session,
			TContract contract,
			Func<Session, TContract, Result<TResultValue>> executeNext) where TContract : class where TResultValue : class
		{
			using (var childSession = Session.Child(session))
			{
				var connection = EnsureConnection(childSession);
				var transaction = EnsureTransaction<TContract, TResultValue>(childSession, connection);

				try
				{
					var result = executeNext(childSession, contract);
					if (result.Message.HasErrors)
					{
						transaction?.Rollback();
					}
					else
					{
						transaction?.Commit();
					}

					return result;
				}
				catch
				{
					transaction?.Rollback();
					throw;
				}
			}
		}
		#endregion

		#region [Private methods]
		[NotNull]
		private IConnection EnsureConnection([NotNull] Session childSession)
		{
			var existsConnection = childSession.TryGetEntry<IConnection>();
			if (existsConnection != null)
			{
				return existsConnection;
			}

			var connection = _connectionManager.Connect();
			childSession.Include(connection);

			return connection;
		}
		[CanBeNull]
		private ITransaction EnsureTransaction<TContract, TResultValue>([NotNull] Session childSession,
			[NotNull] IConnection connection)
			where TContract : class
			where TResultValue : class
		{
			var operationType = _operationProvider.GetUsedOperationType<TContract, TResultValue>();

			var transactionAttribute = operationType.GetCustomAttribute<TransactionalAttribute>();
			if (transactionAttribute == null)
			{
				return null;
			}

			var isolationLevel = transactionAttribute.IsolationLevel ?? _defaultTransactionIsolationLevel;

			var existsTransaction = childSession.TryGetEntry<ITransaction>();
			var transaction = existsTransaction == null
				? connection.StartTransaction(isolationLevel)
				: existsTransaction.StartChildTransaction(isolationLevel);

			childSession.Include(transaction);

			return transaction;
		}
		#endregion
	}
}