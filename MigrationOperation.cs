using System;
using System.Collections.Generic;
using System.Linq;
using EY.Platform.BusinessLogic.Operations;
using EY.Platform.DAL.Infrastructure.Localization;
using EY.Platform.DAL.Infrastructure.Migrations;
using EY.Platform.DAL.Migrations;
using FluentMigrator;
using FluentMigrator.Infrastructure;
using FluentMigrator.Infrastructure.Extensions;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Announcers;
using FluentMigrator.Runner.Initialization;
using FluentMigrator.Runner.Processors;
using JetBrains.Annotations;
using NLog;

namespace EY.Platform.DAL.Infrastructure.Operations
{
	public class MigrationOperation : Operation<MigrationContract, MigrationResult>
	{
		#region [Private fields]
		private static readonly ILogger Logger = LogManager.GetLogger(nameof(MigrationOperation));
		private readonly List<MigrationAssembly> _migrationAssemblies;
		#endregion

		#region [Constructors]
		public MigrationOperation([NotNull] IEnumerable<MigrationAssembly> migrationAssemblies)
		{
			if (migrationAssemblies == null)
			{
				throw new ArgumentNullException(nameof(migrationAssemblies));
			}

			_migrationAssemblies = migrationAssemblies.ToList();

			if (_migrationAssemblies.Count == 0)
			{
				throw new ArgumentException(Errors.MigrationOperation_Constructor_MigrationAssembliesRequired, nameof(migrationAssemblies));
			}
		}
		#endregion

		#region [Override of <Operation<MigrationContract, MigrationResult>>]
		protected override Result<MigrationResult> ExecuteCore(Session session, MigrationContract contract)
		{
			if (contract == null)
			{
				throw new ArgumentNullException(nameof(contract));
			}

			var announcer = new TextWriterAnnouncer(Logger.Debug);
			var migrationContext = new RunnerContext(announcer)
			{
				NestedNamespaces = true
			};

			var factoryProvider = new MigrationProcessorFactoryProvider();
			var factory = factoryProvider.GetFactory(contract.DatabaseType);

			var migrationOptions = new ProcessorOptions
			{
				PreviewOnly = false,
				ProviderSwitches = null,
				Timeout = contract.Timeout
			};

			using (var processor = factory.Create(contract.AdminConnectionString, announcer, migrationOptions))
			{
				var migrationAssemblies = _migrationAssemblies.Select(x => x.Assembly);

				var assemblies = new AssemblyCollection(migrationAssemblies);
				var runner = new MigrationRunner(assemblies, migrationContext, processor);

				runner.Conventions.TypeIsMigration = IsTypeMigration;
				runner.Conventions.GetMigrationInfo = GetPlatformMigrationInfo;

				Logger.Info("Start migration");
				Logger.Info($"-> Database type: {contract.DatabaseType}");
				Logger.Info($"-> Command timeout: {migrationOptions.Timeout}");
				Logger.Info($"-> Assemblies to scan: {string.Join("; ", assemblies.Assemblies.Select(x => x.GetName().Name))}");

				runner.MigrateUp(true);
			}

			var migrationResult = new MigrationResult(_migrationAssemblies);
			return Success(migrationResult);
		}
		private static bool IsTypeMigration(Type type)
		{
			if (typeof(IMigration).IsAssignableFrom(type))
			{
				return type.HasAttribute<MigrationTimestampAttribute>();
			}
			return false;
		}
		private static IMigrationInfo GetPlatformMigrationInfo(Type type)
		{
			Guard.AgainstArgumentIsNull(type, nameof(type));
			Guard.AgainstNull(type.FullName, "type.FullName != null");

			var migrationMetadata = type.GetOneAttribute<MigrationTimestampAttribute>();
			Func<IMigration> migrationFunc = () => (IMigration) type.Assembly.CreateInstance(type.FullName);
			var description = FormattableString.Invariant($"{migrationMetadata.MigrationDate:g} - {type.FullName}");

			var migrationInfo = new MigrationInfo(migrationMetadata.Version, description, TransactionBehavior.Default, migrationFunc);
			foreach (var allAttribute in type.GetAllAttributes<MigrationTraitAttribute>())
			{
				migrationInfo.AddTrait(allAttribute.Name, allAttribute.Value);
			}
			return migrationInfo;
		}
		#endregion
	}
}