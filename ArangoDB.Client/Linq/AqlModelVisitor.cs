﻿using ArangoDB.Client.Common.Remotion.Linq;
using ArangoDB.Client.Common.Remotion.Linq.Clauses;
using ArangoDB.Client.Common.Remotion.Linq.Clauses.Expressions;
using ArangoDB.Client.Common.Remotion.Linq.Clauses.ResultOperators;
using ArangoDB.Client.Data;
using ArangoDB.Client.Linq.Clause;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace ArangoDB.Client.Linq
{
    public class AqlModelVisitor : QueryModelVisitorBase
    {
        public ArangoDatabase Db;

        public StringBuilder QueryText { get; set; }

        public QueryData QueryData { get; set; }

        public QueryModel QueryModel { get; set; }

        public AqlModelVisitor ParnetModelVisitor { get; set; }

        internal int ParameterNameCounter { get; set; }

        internal int GroupByNameCounter { get; set; }

        public AqlModelVisitor(ArangoDatabase db)
        {
            QueryText = new StringBuilder();
            QueryData = new QueryData();

            this.Db = db;
        }

        public override void VisitQueryModel(QueryModel queryModel)
        {
            this.QueryModel = queryModel;

            queryModel.MainFromClause.Accept(this, queryModel);
            VisitBodyClauses(queryModel.BodyClauses, queryModel);
            queryModel.SelectClause.Accept(this, queryModel);
        }

        public override void VisitResultOperator(ResultOperatorBase resultOperator, QueryModel queryModel, int index)
        {
            if (resultOperator as GroupResultOperator != null)
                throw new NotSupportedException("GroupResultOperator");

            if (resultOperator as TakeResultOperator != null)
                throw new NotSupportedException("TakeResultOperator");

            base.VisitResultOperator(resultOperator, queryModel, index);
        }

        public override void VisitAdditionalFromClause(AdditionalFromClause fromClause, QueryModel queryModel, int index)
        {
            string fromName = LinqUtility.ResolveCollectionName(this.Db, fromClause.ItemType);
            QueryText.AppendFormat(" for {0} in {1} ", LinqUtility.ResolvePropertyName(fromClause.ItemName), fromName);
        }

        public override void VisitMainFromClause(MainFromClause fromClause, QueryModel queryModel)
        {
            if (fromClause.FromExpression as SubQueryExpression != null)
                throw new Exception("MainFromClause.FromExpression cant be SubQueryExpression because group by is handle in itself clause class");

            string fromName = LinqUtility.ResolveCollectionName(this.Db, fromClause.ItemType);

            if (fromClause.FromExpression.Type.Name == "IGrouping`2")
            {
                var groupByClause = LinqUtility.PriorGroupBy(ParnetModelVisitor);

                var parentMVisitor = LinqUtility.FindParentModelVisitor(this);
                parentMVisitor.GroupByNameCounter++;

                fromName = groupByClause[0].TranslateIntoName();

                fromName = LinqUtility.ResolvePropertyName(fromName);
            }

            QueryText.AppendFormat(" for {0} in {1} ", LinqUtility.ResolvePropertyName(fromClause.ItemName), fromName);

            base.VisitMainFromClause(fromClause, queryModel);
        }

        public void VisitLetClause(LetClause letClause, QueryModel queryModel, Type lhsType)
        {
            QueryText.AppendFormat(" let `{0}` = ", letClause.ItemName);
            GetAqlExpression(letClause.LetExpression, queryModel);
        }

        public override void VisitSelectClause(SelectClause selectClause, QueryModel queryModel)
        {
            QueryText.Append(" return ");
            GetAqlExpression(selectClause.Selector, queryModel);
        }

        public void VisitFilterClause(FilterClause filterClause, QueryModel queryModel, int index)
        {
            QueryText.Append(" filter ");
            GetAqlExpression(filterClause.Predicate, queryModel);
        }

        public void VisitSkipTakeClause(SkipTakeClause skipTakeClause, QueryModel queryModel, int index)
        {
            if(skipTakeClause.TakeCount==null)
                throw new InvalidOperationException("in limit[skip & take functions] count[take function] should be specified");

            QueryText.AppendFormat("  limit ");
            if (skipTakeClause != null && skipTakeClause.SkipCount!=null)
            {
                GetAqlExpression(skipTakeClause.SkipCount, queryModel);
                QueryText.AppendFormat("  , ");
            }
            GetAqlExpression(skipTakeClause.TakeCount, queryModel);

            //int? skipCount = skipTakeClause.GetConstantSkipCount();
            //int? takeCount = skipTakeClause.GetConstantTakeCount();

            //if (!takeCount.HasValue)
            //    throw new InvalidOperationException("Skip() should be used with Take() in query");

            //if (skipCount.HasValue)
            //    QueryText.AppendFormat("  limit {0},{1} ", skipCount.Value, takeCount.Value);
            //else
            //    QueryText.AppendFormat("  limit {0} ", takeCount.Value);
        }

        public override void VisitWhereClause(WhereClause whereClause, QueryModel queryModel, int index)
        {
            QueryText.Append(" filter ");
            GetAqlExpression(whereClause.Predicate, queryModel);
        }

        public void VisitGroupByClause(GroupByClause groupByClause, QueryModel queryModel, int index)
        {
            var parentMVisitor = LinqUtility.FindParentModelVisitor(this);
            parentMVisitor.GroupByNameCounter++;
            groupByClause.IntoName = "C" + parentMVisitor.GroupByNameCounter;
            groupByClause.funcIntoName = Db.Settings.Linq.TranslateGroupByIntoName;

            QueryText.Append(" collect ");

            var memberExpression = groupByClause.Selector as MemberExpression;
            if (memberExpression != null)
                QueryText.AppendFormat(" {0} = ", LinqUtility.ResolvePropertyName(memberExpression.Member.Name));

            GetAqlExpression(groupByClause.Selector, queryModel, true);

            QueryText.AppendFormat(" into {0} ", LinqUtility.ResolvePropertyName(groupByClause.TranslateIntoName()));

            groupByClause.Visited = true;
        }

        public override void VisitOrderByClause(OrderByClause orderByClause, QueryModel queryModel, int index)
        {
            QueryText.Append(" sort ");
            for (int i = 0; i < orderByClause.Orderings.Count; i++)
            {
                GetAqlExpression(orderByClause.Orderings[i].Expression, queryModel);
                QueryText.AppendFormat(" {0} {1} ",
                    orderByClause.Orderings[i].OrderingDirection == OrderingDirection.Asc ? "asc" : "desc",
                    i != orderByClause.Orderings.Count - 1 ? " , " : string.Empty);
            }

            base.VisitOrderByClause(orderByClause, queryModel, index);
        }

        public override void VisitJoinClause(JoinClause joinClause, QueryModel queryModel, int index)
        {
            base.VisitJoinClause(joinClause, queryModel, index);
        }

        public override void VisitGroupJoinClause(GroupJoinClause groupJoinClause, QueryModel queryModel, int index)
        {

        }

        private void GetAqlExpression(Expression expression, QueryModel queryModel, bool? TreatNewWithoutBracket = false)
        {
            var visitor = new AqlExpressionTreeVisitor(this);
            visitor.TreatNewWithoutBracket = TreatNewWithoutBracket.Value;
            visitor.VisitExpression(expression);
        }
    }
}