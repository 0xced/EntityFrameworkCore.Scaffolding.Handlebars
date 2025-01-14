﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

// Modifications copyright(C) 2020 Tony Sneed.

using System;
using System.Collections.Generic;
using System.Linq;
using EntityFrameworkCore.Scaffolding.Handlebars.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Internal;
using Microsoft.Extensions.Options;

namespace EntityFrameworkCore.Scaffolding.Handlebars
{
    /// <summary>
    /// Generator for the DbContext class using Handlebars templates.
    /// </summary>
    public class HbsCSharpDbContextGenerator : CSharpDbContextGenerator
    {
        private const string EntityLambdaIdentifier = "entity";
        private const string Language = "CSharp";
        private readonly IOptions<HandlebarsScaffoldingOptions> _options;
        private string _modelNamespace;

        /// <summary>
        /// CSharp helper.
        /// </summary>
        protected ICSharpHelper CSharpHelper { get; }

        /// <summary>
        /// Annotation code generator.
        /// </summary>
        protected IAnnotationCodeGenerator AnnotationCodeGenerator { get; }

        /// <summary>
        /// Generator for scaffolding provider.
        /// </summary>
        protected IProviderConfigurationCodeGenerator ProviderConfigurationCodeGenerator { get; }

        /// <summary>
        /// DbContext template service.
        /// </summary>
        protected IDbContextTemplateService DbContextTemplateService { get; }

        /// <summary>
        /// Service for transforming entity definitions.
        /// </summary>
        protected IEntityTypeTransformationService EntityTypeTransformationService { get; }

        /// <summary>
        /// Handlebars template data.
        /// </summary>
        protected Dictionary<string, object> TemplateData { get; set; }

        private bool _entityTypeBuilderInitialized;

        /// <summary>
        /// Constructor for the Handlebars DbContext generator.
        /// </summary>
        /// <param name="providerConfigurationCodeGenerator">Generator for scaffolding provider.</param>
        /// <param name="annotationCodeGenerator">Annotation code generator.</param>
        /// <param name="cSharpHelper">CSharp helper.</param>
        /// <param name="dbContextTemplateService">Template service for DbContext generator.</param>
        /// <param name="entityTypeTransformationService">Service for transforming entity definitions.</param>
        /// <param name="options">Handlebars scaffolding options.</param>
        public HbsCSharpDbContextGenerator(
            [NotNull] IProviderConfigurationCodeGenerator providerConfigurationCodeGenerator, 
            [NotNull] IAnnotationCodeGenerator annotationCodeGenerator,
            [NotNull] IDbContextTemplateService dbContextTemplateService,
            [NotNull] IEntityTypeTransformationService entityTypeTransformationService,
            [NotNull] ICSharpHelper cSharpHelper,
            [NotNull] IOptions<HandlebarsScaffoldingOptions> options)
            : base(providerConfigurationCodeGenerator, annotationCodeGenerator, cSharpHelper)
        {
            ProviderConfigurationCodeGenerator = providerConfigurationCodeGenerator;
            AnnotationCodeGenerator = annotationCodeGenerator;
            CSharpHelper = cSharpHelper;
            DbContextTemplateService = dbContextTemplateService;
            EntityTypeTransformationService = entityTypeTransformationService;
            _options = options;
        }

        /// <summary>
        /// Generate the DbContext class.
        /// </summary>
        /// <param name="model">Metadata about the shape of entities, the relationships between them, and how they map to the database.</param>
        /// <param name="contextName">Name of DbContext class.</param>
        /// <param name="connectionString">Database connection string.</param>
        /// <param name="contextNamespace">Context namespace.</param>
        /// <param name="modelNamespace">Model namespace.</param>
        /// <param name="useDataAnnotations">If false use fluent modeling API.</param>
        /// <param name="suppressConnectionStringWarning">Suppress connection string warning.</param>
        /// <param name="suppressOnConfiguring">Suppress OnConfiguring method.</param>
        /// <returns>DbContext class.</returns>
        public override string WriteCode(IModel model, string contextName, string connectionString, string contextNamespace,
            string modelNamespace, bool useDataAnnotations, bool suppressConnectionStringWarning, bool suppressOnConfiguring)
        {
            Check.NotNull(model, nameof(model));

            if (!string.IsNullOrEmpty(modelNamespace) && string.CompareOrdinal(contextNamespace, modelNamespace) != 0)
                _modelNamespace = modelNamespace;

            TemplateData = new Dictionary<string, object>();

            if (_options.Value.TemplateData != null)
            {
                foreach (KeyValuePair<string, object> entry in _options.Value.TemplateData)
                {
                    TemplateData.Add(entry.Key, entry.Value);
                }
            }

            TemplateData.Add("namespace", contextNamespace);

            GenerateClass(model, contextName, connectionString, useDataAnnotations, suppressConnectionStringWarning, suppressOnConfiguring);

            string output = DbContextTemplateService.GenerateDbContext(TemplateData);
            return output;
        }

        /// <summary>
        /// Generate the DbContext class.
        /// </summary>
        /// <param name="model">Metadata about the shape of entities, the relationships between them, and how they map to the database.</param>
        /// <param name="contextName">Name of DbContext class.</param>
        /// <param name="connectionString">Database connection string.</param>
        /// <param name="useDataAnnotations">Use fluent modeling API if false.</param>
        /// <param name="suppressConnectionStringWarning">Suppress connection string warning.</param>
        /// <param name="suppressOnConfiguring">Suppress OnConfiguring method.</param>
        protected override void GenerateClass([NotNull] IModel model, [NotNull] string contextName, [NotNull] string connectionString,
            bool useDataAnnotations, bool suppressConnectionStringWarning, bool suppressOnConfiguring)
        {
            Check.NotNull(model, nameof(model));
            Check.NotNull(contextName, nameof(contextName));
            Check.NotNull(connectionString, nameof(connectionString));

            if (_options?.Value?.EnableSchemaFolders != true)
                TemplateData.Add("model-namespace", _modelNamespace);
            else
                GenerateModelImports(model);
            TemplateData.Add("class", contextName);
            GenerateDbSets(model);
            GenerateEntityTypeErrors(model);
            if (suppressOnConfiguring)
                TemplateData.Add("suppress-on-configuring", true);
            else
                GenerateOnConfiguring(connectionString, suppressConnectionStringWarning);
            GenerateOnModelCreating(model, useDataAnnotations);
        }

        /// <summary>
        /// Generate OnConfiguring method.
        /// </summary>
        /// <param name="connectionString">Database connection string.</param>
        /// <param name="suppressConnectionStringWarning">Suppress connection string warning.</param>
        protected override void GenerateOnConfiguring([NotNull] string connectionString, bool suppressConnectionStringWarning)
        {
            Check.NotNull(connectionString, nameof(connectionString));

            var onConfiguring = GetOnConfiguring(connectionString, suppressConnectionStringWarning);
            TemplateData.Add("on-configuring", onConfiguring);

            TemplateData.Add("connection-string", connectionString);
            TemplateData.Add("connectionstring-warning", $"#warning {DesignStrings.SensitiveInformationWarning}");

            var useProviderCall = ProviderConfigurationCodeGenerator.GenerateUseProvider(
                connectionString, ProviderConfigurationCodeGenerator.GenerateProviderOptions());
            var contextOptions = ProviderConfigurationCodeGenerator.GenerateContextOptions();
            if (contextOptions != null)
            {
                useProviderCall = useProviderCall.Chain(contextOptions);
            }

            TemplateData.Add("options-builder-provider", CSharpHelper.Fragment(useProviderCall));
        }

        /// <summary>
        /// Generate OnModelBuilding method.
        /// </summary>
        /// <param name="model">Metadata about the shape of entities, the relationships between them, and how they map to the database.</param>
        /// <param name="useDataAnnotations">Use fluent modeling API if false.</param>
        protected override void GenerateOnModelCreating([NotNull] IModel model, bool useDataAnnotations)
        {
            Check.NotNull(model, nameof(model));

            var sb = new IndentedStringBuilder();
            using (sb.Indent())
            using (sb.Indent())
            {
                sb.AppendLine("protected override void OnModelCreating(ModelBuilder modelBuilder)");
                sb.Append("{");

                var annotations = AnnotationCodeGenerator
                    .FilterIgnoredAnnotations(model.GetAnnotations())
                    .ToDictionary(a => a.Name, a => a);

                AnnotationCodeGenerator.RemoveAnnotationsHandledByConventions(model, annotations);

                annotations.Remove(CoreAnnotationNames.ProductVersion);
                annotations.Remove(RelationalAnnotationNames.MaxIdentifierLength);
                annotations.Remove(ScaffoldingAnnotationNames.DatabaseName);
                annotations.Remove(ScaffoldingAnnotationNames.EntityTypeErrors);

                var lines = new List<string>();

                lines.AddRange(
                    AnnotationCodeGenerator.GenerateFluentApiCalls(model, annotations).Select(m => CSharpHelper.Fragment(m))
                        .Concat(GenerateAnnotations(annotations.Values)));

                if (lines.Count > 0)
                {
                    using (sb.Indent())
                    {
                        sb.AppendLine();
                        sb.Append("modelBuilder" + lines[0]);

                        using (sb.Indent())
                        {
                            foreach (var line in lines.Skip(1))
                            {
                                sb.AppendLine();
                                sb.Append(line);
                            }
                        }

                        sb.AppendLine(";");
                    }
                }

                using (sb.Indent())
                {

                    foreach (var entityType in model.GetScaffoldEntityTypes(_options.Value))
                    {
                        _entityTypeBuilderInitialized = false;

                        GenerateEntityType(entityType, useDataAnnotations, sb);

                        if (_entityTypeBuilderInitialized)
                        {
                            sb.AppendLine("});");
                        }
                    }

                    foreach (var sequence in model.GetSequences())
                    {
                        GenerateSequence(sequence, sb);
                    }
                }

                sb.AppendLine();
                using (sb.Indent())
                {
                    sb.AppendLine("OnModelCreatingPartial(modelBuilder);");
                }
                sb.AppendLine("}");
                sb.AppendLine();
                sb.Append("partial void OnModelCreatingPartial(ModelBuilder modelBuilder);");
            }

            var onModelCreating = sb.ToString();
            TemplateData.Add("on-model-creating", onModelCreating);
        }

        private void GenerateDbSets(IModel model)
        {
            var dbSets = new List<Dictionary<string, object>>();

            foreach (var entityType in model.GetScaffoldEntityTypes(_options.Value))
            {
                var transformedEntityTypeName = GetEntityTypeName(
                    entityType, EntityTypeTransformationService.TransformTypeEntityName(entityType.Name));
                dbSets.Add(new Dictionary<string, object>
                {
                    { "set-property-type", transformedEntityTypeName },
                    { "set-property-name", entityType.GetDbSetName() },
                    { "nullable-reference-types",  _options?.Value?.EnableNullableReferenceTypes == true }
                });
            }

            TemplateData.Add("dbsets", dbSets);
        }

        private void GenerateModelImports(IModel model)
        {
            var modelImports = new List<Dictionary<string, object>>();
            var schemas = model.GetScaffoldEntityTypes(_options.Value)
                .Select(e => e.GetSchema())
                .OrderBy(s => s)
                .Distinct();

            foreach (var schema in schemas)
            {
                modelImports.Add(new Dictionary<string, object>
                {
                    { "model-import", $"{schema} = {_modelNamespace}.{schema}"}
                });
            }

            TemplateData.Add("model-imports", modelImports);
        }

        private void GenerateEntityTypeErrors(IModel model)
        {
            var entityTypeErrors = new List<Dictionary<string, object>>();

            foreach (var entityTypeError in model.GetEntityTypeErrors())
            {
                entityTypeErrors.Add(new Dictionary<string, object>
                {
                    { "entity-type-error", $"// {entityTypeError.Value} Please see the warning messages." },
                });
            }

            TemplateData.Add("entity-type-errors", entityTypeErrors);
        }

        private void InitializeEntityTypeBuilder(IEntityType entityType, IndentedStringBuilder sb)
        {
            if (!_entityTypeBuilderInitialized)
            {
                var transformedEntityTypeName = GetEntityTypeName(
                    entityType, EntityTypeTransformationService.TransformTypeEntityName(entityType.Name));

                sb.AppendLine();
                sb.AppendLine($"modelBuilder.Entity<{transformedEntityTypeName}>({EntityLambdaIdentifier} =>");
                sb.Append("{");
            }

            _entityTypeBuilderInitialized = true;
        }

        private string GetEntityTypeName(IEntityType entityType, string entityTypeName)
        {
            var schema = !string.IsNullOrEmpty(entityType.GetTableName())
                ? entityType.GetSchema()
                : entityType.GetViewSchema();
            
            return _options?.Value?.EnableSchemaFolders == true
                ? $"{schema}.{entityTypeName}" : entityTypeName;
        }

        private void GenerateEntityType(IEntityType entityType, bool useDataAnnotations, IndentedStringBuilder sb)
        {
            GenerateKey(entityType.FindPrimaryKey(), entityType, useDataAnnotations, sb);

            var annotations = AnnotationCodeGenerator
                .FilterIgnoredAnnotations(entityType.GetAnnotations())
                .ToDictionary(a => a.Name, a => a);
            AnnotationCodeGenerator.RemoveAnnotationsHandledByConventions(entityType, annotations);

            annotations.Remove(RelationalAnnotationNames.TableName);
            annotations.Remove(RelationalAnnotationNames.Schema);
            annotations.Remove(RelationalAnnotationNames.ViewName);
            annotations.Remove(RelationalAnnotationNames.ViewSchema);
            annotations.Remove(ScaffoldingAnnotationNames.DbSetName);
            annotations.Remove(RelationalAnnotationNames.ViewDefinitionSql);

            if (useDataAnnotations)
            {
                // Strip out any annotations handled as attributes - these are already handled when generating
                // the entity's properties
                _ = AnnotationCodeGenerator.GenerateDataAnnotationAttributes(entityType, annotations);
            }

            if (!useDataAnnotations || entityType.GetViewName() != null)
            {
                GenerateTableName(entityType, sb);
            }

            var lines = new List<string>(
                AnnotationCodeGenerator.GenerateFluentApiCalls(entityType, annotations).Select(m => CSharpHelper.Fragment(m))
                    .Concat(GenerateAnnotations(annotations.Values)));

            AppendMultiLineFluentApi(entityType, lines, sb);

            foreach (var index in entityType.GetIndexes())
            {
                // If there are annotations that cannot be represented using an IndexAttribute then use fluent API even
                // if useDataAnnotations is true.
                var indexAnnotations = AnnotationCodeGenerator
                    .FilterIgnoredAnnotations(index.GetAnnotations())
                    .ToDictionary(a => a.Name, a => a);
                AnnotationCodeGenerator.RemoveAnnotationsHandledByConventions(index, indexAnnotations);

                if (!useDataAnnotations || indexAnnotations.Count > 0)
                {
                    GenerateIndex(index, sb);
                }
            }

            foreach (var property in entityType.GetProperties())
            {
                GenerateProperty(property, useDataAnnotations, sb);
            }

            foreach (var foreignKey in entityType.GetScaffoldForeignKeys(_options.Value))
            {
                GenerateRelationship(foreignKey, useDataAnnotations, sb);
            }
        }

        private void AppendMultiLineFluentApi(IEntityType entityType, IList<string> lines, IndentedStringBuilder sb)
        {
            if (lines.Count <= 0)
            {
                return;
            }

            InitializeEntityTypeBuilder(entityType, sb);

            using (sb.Indent())
            {
                sb.AppendLine();

                sb.Append(EntityLambdaIdentifier + lines[0]);

                using (sb.Indent())
                {
                    foreach (var line in lines.Skip(1))
                    {
                        sb.AppendLine();
                        sb.Append(line);
                    }
                }

                sb.AppendLine(";");
            }
        }

        private void GenerateKey(IKey key, IEntityType entityType, bool useDataAnnotations, IndentedStringBuilder sb)
        {
            if (key == null)
            {
                if (!useDataAnnotations)
                {
                    var line = new List<string> { $".{nameof(EntityTypeBuilder.HasNoKey)}()" };

                    AppendMultiLineFluentApi(entityType, line, sb);
                }

                return;
            }

            var annotations = AnnotationCodeGenerator
                .FilterIgnoredAnnotations(key.GetAnnotations())
                .ToDictionary(a => a.Name, a => a);
            AnnotationCodeGenerator.RemoveAnnotationsHandledByConventions(key, annotations);

            var explicitName = key.GetName() != key.GetDefaultName();
            annotations.Remove(RelationalAnnotationNames.Name);

            if (key.Properties.Count == 1
                && annotations.Count == 0)
            {
                if (key is Key concreteKey
                    && key.Properties.SequenceEqual(
                        KeyDiscoveryConvention.DiscoverKeyProperties(
                            concreteKey.DeclaringEntityType,
                            concreteKey.DeclaringEntityType.GetProperties())))
                {
                    return;
                }

                if (!explicitName
                    && useDataAnnotations)
                {
                    return;
                }
            }

            var lines = new List<string>
            {
                $".{nameof(EntityTypeBuilder.HasKey)}(e => {GenerateLambdaToKey(key.Properties, "e", EntityTypeTransformationService.TransformPropertyName)})"
            };

            if (explicitName)
            {
                lines.Add(
                    $".{nameof(RelationalKeyBuilderExtensions.HasName)}" +
                    $"({CSharpHelper.Literal(key.GetName())})");
            }

            lines.AddRange(
                AnnotationCodeGenerator.GenerateFluentApiCalls(key, annotations).Select(m => CSharpHelper.Fragment(m))
                    .Concat(GenerateAnnotations(annotations.Values)));

            AppendMultiLineFluentApi(key.DeclaringEntityType, lines, sb);
        }

        private void GenerateTableName(IEntityType entityType, IndentedStringBuilder sb)
        {
            var tableName = entityType.GetTableName();
            var schema = entityType.GetSchema();
            var defaultSchema = entityType.Model.GetDefaultSchema();

            var explicitSchema = schema != null && schema != defaultSchema;
            var explicitTable = explicitSchema || tableName != null && tableName != entityType.GetDbSetName();
            if (explicitTable)
            {
                var parameterString = CSharpHelper.Literal(tableName);
                if (explicitSchema)
                {
                    parameterString += ", " + CSharpHelper.Literal(schema);
                }

                var lines = new List<string> { $".{nameof(RelationalEntityTypeBuilderExtensions.ToTable)}({parameterString})" };

                AppendMultiLineFluentApi(entityType, lines, sb);
            }

            var viewName = entityType.GetViewName();
            var viewSchema = entityType.GetViewSchema();

            var explicitViewSchema = viewSchema != null && viewSchema != defaultSchema;
            var explicitViewTable = explicitViewSchema || viewName != null;

            if (explicitViewTable)
            {
                var parameterString = CSharpHelper.Literal(viewName);
                if (explicitViewSchema)
                {
                    parameterString += ", " + CSharpHelper.Literal(viewSchema);
                }

                var lines = new List<string> { $".{nameof(RelationalEntityTypeBuilderExtensions.ToView)}({parameterString})" };

                AppendMultiLineFluentApi(entityType, lines, sb);
            }
        }

        private void GenerateIndex(IIndex index, IndentedStringBuilder sb)
        {
            var annotations = AnnotationCodeGenerator
                .FilterIgnoredAnnotations(index.GetAnnotations())
                .ToDictionary(a => a.Name, a => a);
            AnnotationCodeGenerator.RemoveAnnotationsHandledByConventions(index, annotations);

            var lines = new List<string>
            {
                $".{nameof(EntityTypeBuilder.HasIndex)}(e => {GenerateLambdaToKey(index.Properties, "e", EntityTypeTransformationService.TransformPropertyName)})"
            };
            annotations.Remove(RelationalAnnotationNames.Name);

            if (index.IsUnique)
            {
                lines.Add($".{nameof(IndexBuilder.IsUnique)}()");
            }

            lines.AddRange(
                AnnotationCodeGenerator.GenerateFluentApiCalls(index, annotations).Select(m => CSharpHelper.Fragment(m))
                    .Concat(GenerateAnnotations(annotations.Values)));

            AppendMultiLineFluentApi(index.DeclaringEntityType, lines, sb);
        }

        private void GenerateProperty(IProperty property, bool useDataAnnotations, IndentedStringBuilder sb)
        {
            var lines = new List<string>
            {
                $".{nameof(EntityTypeBuilder.Property)}(e => e.{EntityTypeTransformationService.TransformPropertyName(property.Name, property.DeclaringType.Name)})"
            };

            var annotations = AnnotationCodeGenerator
                .FilterIgnoredAnnotations(property.GetAnnotations())
                .ToDictionary(a => a.Name, a => a);
            AnnotationCodeGenerator.RemoveAnnotationsHandledByConventions(property, annotations);
            annotations.Remove(ScaffoldingAnnotationNames.ColumnOrdinal);

            if (useDataAnnotations)
            {
                // Strip out any annotations handled as attributes - these are already handled when generating
                // the entity's properties
                // Only relational ones need to be removed here. Core ones are already removed by FilterIgnoredAnnotations
                annotations.Remove(RelationalAnnotationNames.ColumnName);
                annotations.Remove(RelationalAnnotationNames.ColumnType);

                _ = AnnotationCodeGenerator.GenerateDataAnnotationAttributes(property, annotations);
            }
            else
            {
                if (!property.IsNullable
                    && property.ClrType.IsNullableType()
                    && !property.IsPrimaryKey())
                {
                    lines.Add($".{nameof(PropertyBuilder.IsRequired)}()");
                }

                var columnType = property.GetConfiguredColumnType();
                if (columnType != null)
                {
                    lines.Add(
                        $".{nameof(RelationalPropertyBuilderExtensions.HasColumnType)}({CSharpHelper.Literal(columnType)})");
                    annotations.Remove(RelationalAnnotationNames.ColumnType);
                }

                var maxLength = property.GetMaxLength();
                if (maxLength.HasValue)
                {
                    lines.Add(
                        $".{nameof(PropertyBuilder.HasMaxLength)}({CSharpHelper.Literal(maxLength.Value)})");
                }
            }

            var precision = property.GetPrecision();
            var scale = property.GetScale();
            if (precision != null && scale != null && scale != 0)
            {
                lines.Add(
                    $".{nameof(PropertyBuilder.HasPrecision)}({CSharpHelper.Literal(precision.Value)}, {CSharpHelper.Literal(scale.Value)})");
            }
            else if (precision != null)
            {
                lines.Add(
                    $".{nameof(PropertyBuilder.HasPrecision)}({CSharpHelper.Literal(precision.Value)})");
            }

            if (property.IsUnicode() != null)
            {
                lines.Add(
                    $".{nameof(PropertyBuilder.IsUnicode)}({(property.IsUnicode() == false ? "false" : "")})");
            }

            var defaultValue = property.GetDefaultValue();
            if (defaultValue == DBNull.Value)
            {
                lines.Add($".{nameof(RelationalPropertyBuilderExtensions.HasDefaultValue)}()");
                annotations.Remove(RelationalAnnotationNames.DefaultValue);
            }
            else if (defaultValue != null)
            {
                lines.Add(
                    $".{nameof(RelationalPropertyBuilderExtensions.HasDefaultValue)}({CSharpHelper.UnknownLiteral(defaultValue)})");
                annotations.Remove(RelationalAnnotationNames.DefaultValue);
            }

            var valueGenerated = property.ValueGenerated;
            var isRowVersion = false;
            if (((IConventionProperty)property).GetValueGeneratedConfigurationSource() is ConfigurationSource
                valueGeneratedConfigurationSource
                && valueGeneratedConfigurationSource != ConfigurationSource.Convention
                && ValueGenerationConvention.GetValueGenerated(property) != valueGenerated)
            {
                var methodName = valueGenerated switch
                {
                    ValueGenerated.OnAdd => nameof(PropertyBuilder.ValueGeneratedOnAdd),
                    ValueGenerated.OnAddOrUpdate => property.IsConcurrencyToken
                        ? nameof(PropertyBuilder.IsRowVersion)
                        : nameof(PropertyBuilder.ValueGeneratedOnAddOrUpdate),
                    ValueGenerated.OnUpdate => nameof(PropertyBuilder.ValueGeneratedOnUpdate),
                    ValueGenerated.Never => nameof(PropertyBuilder.ValueGeneratedNever),
                    _ => throw new InvalidOperationException(DesignStrings.UnhandledEnumValue($"{nameof(ValueGenerated)}.{valueGenerated}"))
                };

                lines.Add($".{methodName}()");
            }

            if (property.IsConcurrencyToken
                && !isRowVersion)
            {
                lines.Add($".{nameof(PropertyBuilder.IsConcurrencyToken)}()");
            }

            lines.AddRange(
                AnnotationCodeGenerator.GenerateFluentApiCalls(property, annotations).Select(m => CSharpHelper.Fragment(m))
                    .Concat(GenerateAnnotations(annotations.Values)));

            switch (lines.Count)
            {
                case 1:
                    return;
                case 2:
                    lines = new List<string> { lines[0] + lines[1] };
                    break;
            }

            AppendMultiLineFluentApi(property.DeclaringEntityType, lines, sb);
        }

        private void GenerateRelationship(IForeignKey foreignKey, bool useDataAnnotations, IndentedStringBuilder sb)
        {
            var canUseDataAnnotations = true;
            var annotations = AnnotationCodeGenerator
                .FilterIgnoredAnnotations(foreignKey.GetAnnotations())
                .ToDictionary(a => a.Name, a => a);
            AnnotationCodeGenerator.RemoveAnnotationsHandledByConventions(foreignKey, annotations);

            var lines = new List<string>
            {
                $".{nameof(EntityTypeBuilder.HasOne)}("
                + (foreignKey.DependentToPrincipal != null ? $"d => d.{EntityTypeTransformationService.TransformNavPropertyName(foreignKey.DependentToPrincipal?.Name, foreignKey.PrincipalToDependent?.DeclaringType.Name)}" : null)
                + ")",
                $".{(foreignKey.IsUnique ? nameof(ReferenceNavigationBuilder.WithOne) : nameof(ReferenceNavigationBuilder.WithMany))}"
                + "("
                + (foreignKey.PrincipalToDependent != null ? $"p => p.{EntityTypeTransformationService.TransformNavPropertyName(foreignKey.PrincipalToDependent?.Name, foreignKey.DependentToPrincipal?.DeclaringType.Name)}" : null)
                + ")"
            };

            if (!foreignKey.PrincipalKey.IsPrimaryKey())
            {
                canUseDataAnnotations = false;
                lines.Add(
                    $".{nameof(ReferenceReferenceBuilder.HasPrincipalKey)}"
                    + (foreignKey.IsUnique ? $"<{EntityTypeTransformationService.TransformPropertyName(((ITypeBase)foreignKey.PrincipalEntityType).DisplayName(), "")}>" : "")
                    + $"(p => {GenerateLambdaToKey(foreignKey.PrincipalKey.Properties, "p", EntityTypeTransformationService.TransformNavPropertyName)})");
            }

            lines.Add(
                $".{nameof(ReferenceReferenceBuilder.HasForeignKey)}"
                + (foreignKey.IsUnique ? $"<{GetEntityTypeName(foreignKey.DeclaringEntityType, EntityTypeTransformationService.TransformTypeEntityName(((ITypeBase)foreignKey.DeclaringEntityType).DisplayName()))}>" : "")
                + $"(d => {GenerateLambdaToKey(foreignKey.Properties, "d", EntityTypeTransformationService.TransformPropertyName)})");

            var defaultOnDeleteAction = foreignKey.IsRequired
                ? DeleteBehavior.Cascade
                : DeleteBehavior.ClientSetNull;

            if (foreignKey.DeleteBehavior != defaultOnDeleteAction)
            {
                canUseDataAnnotations = false;
                lines.Add(
                    $".{nameof(ReferenceReferenceBuilder.OnDelete)}({CSharpHelper.Literal(foreignKey.DeleteBehavior)})");
            }

            if (!string.IsNullOrEmpty((string)foreignKey[RelationalAnnotationNames.Name]))
            {
                canUseDataAnnotations = false;
            }

            lines.AddRange(
                AnnotationCodeGenerator.GenerateFluentApiCalls(foreignKey, annotations).Select(m => CSharpHelper.Fragment(m))
                    .Concat(GenerateAnnotations(annotations.Values)));

            if (!useDataAnnotations
                || !canUseDataAnnotations)
            {
                AppendMultiLineFluentApi(foreignKey.DeclaringEntityType, lines, sb);
            }
        }

        private void GenerateSequence(ISequence sequence, IndentedStringBuilder sb)
        {
            var methodName = nameof(RelationalModelBuilderExtensions.HasSequence);

            if (sequence.Type != Sequence.DefaultClrType)
            {
                methodName += $"<{CSharpHelper.Reference(sequence.Type)}>";
            }

            var parameters = CSharpHelper.Literal(sequence.Name);

            if (!string.IsNullOrEmpty(sequence.Schema)
                && sequence.Model.GetDefaultSchema() != sequence.Schema)
            {
                parameters += $", {CSharpHelper.Literal(sequence.Schema)}";
            }

            var lines = new List<string> { $"modelBuilder.{methodName}({parameters})" };

            if (sequence.StartValue != Sequence.DefaultStartValue)
            {
                lines.Add($".{nameof(SequenceBuilder.StartsAt)}({sequence.StartValue})");
            }

            if (sequence.IncrementBy != Sequence.DefaultIncrementBy)
            {
                lines.Add($".{nameof(SequenceBuilder.IncrementsBy)}({sequence.IncrementBy})");
            }

            if (sequence.MinValue != Sequence.DefaultMinValue)
            {
                lines.Add($".{nameof(SequenceBuilder.HasMin)}({sequence.MinValue})");
            }

            if (sequence.MaxValue != Sequence.DefaultMaxValue)
            {
                lines.Add($".{nameof(SequenceBuilder.HasMax)}({sequence.MaxValue})");
            }

            if (sequence.IsCyclic != Sequence.DefaultIsCyclic)
            {
                lines.Add($".{nameof(SequenceBuilder.IsCyclic)}()");
            }

            if (lines.Count == 2)
            {
                lines = new List<string> { lines[0] + lines[1] };
            }

            sb.AppendLine();
            sb.Append(lines[0]);

            using (sb.Indent())
            {
                foreach (var line in lines.Skip(1))
                {
                    sb.AppendLine();
                    sb.Append(line);
                }
            }

            sb.AppendLine(";");
        }

        private string GetOnConfiguring(string connectionString, bool suppressConnectionStringWarning)
        {
            var sb = new IndentedStringBuilder();
            using (sb.Indent())
            using (sb.Indent())
            {
                sb.AppendLine("protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)");
                sb.AppendLine("{");

                using (sb.Indent())
                {
                    sb.AppendLine("if (!optionsBuilder.IsConfigured)");
                    sb.AppendLine("{");

                    using (sb.Indent())
                    {
                        if (!suppressConnectionStringWarning)
                        {
                            sb.DecrementIndent()
                                .DecrementIndent()
                                .DecrementIndent()
                                .DecrementIndent()
                                .AppendLine("#warning " + DesignStrings.SensitiveInformationWarning)
                                .IncrementIndent()
                                .IncrementIndent()
                                .IncrementIndent()
                                .IncrementIndent();
                        }

                        sb.Append("optionsBuilder");

                        var useProviderCall = ProviderConfigurationCodeGenerator.GenerateUseProvider(connectionString);

                        sb.Append(CSharpHelper.Fragment(useProviderCall))
                          .AppendLine(";");
                    }
                    sb.AppendLine("}");
                }

                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private static string GenerateLambdaToKey(
            IReadOnlyList<IProperty> properties,
            string lambdaIdentifier,
            Func<string, string, string> nameTransform)
        {
            return properties.Count <= 0
                ? ""
                : properties.Count == 1
                ? $"{lambdaIdentifier}.{nameTransform(properties[0].Name, properties[0].DeclaringType.Name)}"
                : $"new {{ {string.Join(", ", properties.Select(p => lambdaIdentifier + "." + nameTransform(p.Name, p.DeclaringType.Name)))} }}";
        }

        private static void RemoveAnnotation(ref List<IAnnotation> annotations, string annotationName)
            => annotations.Remove(annotations.SingleOrDefault(a => a.Name == annotationName));

        private IList<string> GenerateAnnotations(IEnumerable<IAnnotation> annotations)
            => annotations.Select(GenerateAnnotation).ToList();

        private string GenerateAnnotation(IAnnotation annotation)
            => $".HasAnnotation({CSharpHelper.Literal(annotation.Name)}, " +
               $"{CSharpHelper.UnknownLiteral(annotation.Value)})";
    }
}
