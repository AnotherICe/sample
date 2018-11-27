using System.Collections.Generic;

using Aspose.Cells;

using EY.CFC.Contracts.Parse;
using EY.CFC.Data.DataTransform.Aspose.Extensions;
using EY.CFC.Mapping;
using EY.Platform;
using EY.Platform.DataTransform.BusinessLogic.Pipeline;

using JetBrains.Annotations;

namespace EY.CFC.Data.DataTransform.Aspose.Support
{
	public class MappingTablePipelineFactory : IMappingTablePipelineFactory
	{
		#region [Fields]
		private readonly IExtractPrimitivePipelineSetupHelper _extractPrimitivePipelineSetupHelper;
		private readonly IPropertySetterProvider _propertySetterProvider;
		#endregion

		#region [c-tor]
		public MappingTablePipelineFactory(
			[NotNull] IPropertySetterProvider propertySetterProvider, 
			[NotNull] IExtractPrimitivePipelineSetupHelper extractPrimitivePipelineSetupHelper)
		{
			Guard.AgainstArgumentIsNull(propertySetterProvider, nameof(propertySetterProvider));
			Guard.AgainstArgumentIsNull(extractPrimitivePipelineSetupHelper, nameof(extractPrimitivePipelineSetupHelper));

			_propertySetterProvider = propertySetterProvider;
			_extractPrimitivePipelineSetupHelper = extractPrimitivePipelineSetupHelper;
		}
		#endregion

		#region IMappingTablePipelineFactory implementation
		public Pipeline<MappingTableParseInput, MappingTableParseModel, Workbook, IReadOnlyDictionary<IParseAddress, TContract>> Create<TContract>(ExcelRecordMapping mapping)
			where TContract : new()
		{
			var rowPipeline = AsposePipelineExtensions.StartPipeline<MappingTableParseInput, MappingTableParseModel, Row>()
				.WithModel(() => new TContract())
				.Aggregate(mapping.Properties, PipeContractProperty)
				.ExtractModel();

			return AsposePipelineExtensions.StartPipeline<MappingTableParseInput, MappingTableParseModel, Workbook>()
				.Worksheet(mapping.WorksheetName)
				.Rows(mapping.StartRow)
				.Transform(rowPipeline.Run);
		}
		#endregion

		#region [Private]
		#region [Private methods]
		private Pipeline<MappingTableParseInput, MappingTableParseModel, Row, RowWithModel<TContract>> PipeContractProperty<TContract>(
			Pipeline<MappingTableParseInput, MappingTableParseModel, Row, RowWithModel<TContract>> previous, 
			ExcelRecordMappingProperty property)
			where TContract : new()
		{
			var propertySetter = _propertySetterProvider.For(typeof(TContract), property.PropertyPath);

			var propertyCellPipeline = AsposePipelineExtensions.StartPipeline<MappingTableParseInput, MappingTableParseModel, Row>().Cell(property.ColumnName);
			var propertyValuePipeline = _extractPrimitivePipelineSetupHelper.For(propertyCellPipeline, propertySetter.RequiredType);
			var propertyAddressPipeline = propertyCellPipeline.Address();

			return previous.SetProperty(
				propertySetter,
				(x, y, z) => z.Model,
				(x, y, z) => new ValueWithAddress<object>
				{
					Address = propertyAddressPipeline.Run(x, y, z.Row),
					Value = propertyValuePipeline.Run(x, y, z.Row)
				});
		}
		#endregion
		#endregion
	}
}