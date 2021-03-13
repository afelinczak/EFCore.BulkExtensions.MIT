﻿using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EFCore.BulkExtensions
{
    internal static class DbContextBulkTransactionGraphUtil
    {
        public static void ExecuteWithGraph(DbContext context, IEnumerable<object> entities, OperationType operationType, BulkConfig bulkConfig, Action<decimal> progress)
        {
            if (operationType != OperationType.Insert
                        && operationType != OperationType.InsertOrUpdate
                        && operationType != OperationType.InsertOrUpdateDelete
                        && operationType != OperationType.Update)
                throw new InvalidBulkConfigException($"{nameof(BulkConfig)}.{nameof(BulkConfig.IncludeGraph)} only supports Insert or Update operations.");

            // If this is set to false, won't be able to propogate new primary keys to the relationships
            bulkConfig.SetOutputIdentity = true;

            var rootGraphItems = GraphUtil.GetOrderedGraph(context, entities);

            if (rootGraphItems == null)
                return;

            foreach (var actionGraphItem in rootGraphItems)
            {
                var entitiesToAction = actionGraphItem.Entities.Select(y => y.Entity).ToList();
                var tableInfo = TableInfo.CreateInstance(context, actionGraphItem.EntityClrType, entitiesToAction, operationType, bulkConfig);

                SqlBulkOperation.Merge(context, actionGraphItem.EntityClrType, entitiesToAction, tableInfo, operationType, progress);

                // Loop through the dependants and update their foreign keys with the PK values of the just inserted / merged entities
                foreach (var graphEntity in actionGraphItem.Entities)
                {
                    var entity = graphEntity.Entity;
                    var parentEntity = graphEntity.ParentEntity;

                    // If the parent entity is null its the root type of the object graph.
                    if (parentEntity is null)
                    {
                        continue;
                    }
                    else
                    {
                        var navigation = actionGraphItem.ParentNavigation;

                        if (navigation.IsDependentToPrincipal())
                        {
                            SetForeignKeyForRelationship(context, navigation,
                                dependent: parentEntity,
                                principal: entity);
                        }
                        else
                        {
                            SetForeignKeyForRelationship(context, navigation,
                                dependent: entity,
                                principal: parentEntity);
                        }
                    }
                }
            }
        }


        public static async Task ExecuteWithGraphAsync(DbContext context, IEnumerable<object> entities, OperationType operationType, BulkConfig bulkConfig, Action<decimal> progress, CancellationToken cancellationToken)
        {
            if (operationType != OperationType.Insert
                        && operationType != OperationType.InsertOrUpdate
                        && operationType != OperationType.InsertOrUpdateDelete
                        && operationType != OperationType.Update)
                throw new InvalidBulkConfigException($"{nameof(BulkConfig)}.{nameof(BulkConfig.IncludeGraph)} only supports Insert or Update operations.");

            // If this is set to false, won't be able to propogate new primary keys to the relationships
            bulkConfig.SetOutputIdentity = true;

            var rootGraphItems = GraphUtil.GetOrderedGraph(context, entities);

            if (rootGraphItems == null)
                return;

            foreach (var actionGraphItem in rootGraphItems)
            {
                var entitiesToAction = actionGraphItem.Entities.Select(y => y.Entity).ToList();
                var tableInfo = TableInfo.CreateInstance(context, actionGraphItem.EntityClrType, entitiesToAction, operationType, bulkConfig);

                await SqlBulkOperation.MergeAsync(context, actionGraphItem.EntityClrType, entitiesToAction, tableInfo, operationType, progress, cancellationToken);

                // Loop through the dependants and update their foreign keys with the PK values of the just inserted / merged entities
                foreach (var graphEntity in actionGraphItem.Entities)
                {
                    var entity = graphEntity.Entity;
                    var parentEntity = graphEntity.ParentEntity;

                    // If the parent entity is null its the root type of the object graph.
                    if (parentEntity is null)
                    {
                        continue;
                    }
                    else
                    {
                        var navigation = actionGraphItem.ParentNavigation;

                        if (navigation.IsDependentToPrincipal())
                        {
                            SetForeignKeyForRelationship(context, navigation,
                                dependent: parentEntity,
                                principal: entity);
                        }
                        else
                        {
                            SetForeignKeyForRelationship(context, navigation,
                                dependent: entity,
                                principal: parentEntity);
                        }
                    }
                }
            }
        }

        private static void SetForeignKeyForRelationship(DbContext context, INavigation navigation, object dependent, object principal)
        {
            var principalKeyProperties = navigation.ForeignKey.PrincipalKey.Properties;
            var pkValues = new List<object>();

            foreach (var pk in principalKeyProperties)
            {
                var value = context.Entry(principal).Property(pk.Name).CurrentValue;
                pkValues.Add(value);
            }

            var dependantKeyProperties = navigation.ForeignKey.Properties;

            for (int i = 0; i < pkValues.Count; i++)
            {
                var dk = dependantKeyProperties[i];
                var pkVal = pkValues[i];

                context.Entry(dependent).Property(dk.Name).CurrentValue = pkVal;
            }
        }
    }
}
