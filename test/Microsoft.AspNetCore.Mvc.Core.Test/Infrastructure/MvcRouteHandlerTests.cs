// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Internal;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Tree;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Mvc.Infrastructure
{
    public class MvcRouteHandlerTests
    {
        [Fact]
        public async Task RouteHandler_Success_LogsCorrectValues()
        {
            // Arrange
            var sink = new TestSink();
            var loggerFactory = new TestLoggerFactory(sink, enabled: true);

            var displayName = "A.B.C";
            var actionDescriptor = new Mock<ActionDescriptor>();
            actionDescriptor
                .SetupGet(ad => ad.DisplayName)
                .Returns(displayName);

            var context = CreateRouteContext();

            var handler = CreateMvcRouteHandler(actionDescriptor: actionDescriptor.Object, loggerFactory: loggerFactory);
            await handler.RouteAsync(context);

            // Act
            await context.Handler(context.HttpContext);

            // Assert
            Assert.Single(sink.Scopes);
            Assert.Equal(displayName, sink.Scopes[0].Scope?.ToString());

            Assert.Equal(2, sink.Writes.Count);
            Assert.Equal($"Executing action {displayName}", sink.Writes[0].State?.ToString());
            // This message has the execution time embedded, which we don't want to verify.
            Assert.StartsWith($"Executed action {displayName} ", sink.Writes[1].State?.ToString());
        }

        [Fact]
        public async Task RouteAsync_FailOnNoAction_LogsCorrectValues()
        {
            // Arrange
            var sink = new TestSink();
            var loggerFactory = new TestLoggerFactory(sink, enabled: true);

            var mockActionSelector = new Mock<IActionSelector>();
            mockActionSelector
                .Setup(a => a.Select(It.IsAny<RouteContext>()))
                .Returns<ActionDescriptor>(null);

            var context = CreateRouteContext();

            var handler = CreateMvcRouteHandler(
                actionSelector: mockActionSelector.Object,
                loggerFactory: loggerFactory);

            var expectedMessage = "No actions matched the current request";

            // Act
            await handler.RouteAsync(context);

            // Assert
            Assert.Empty(sink.Scopes);
            Assert.Single(sink.Writes);
            Assert.Equal(expectedMessage, sink.Writes[0].State?.ToString());
        }

        [Fact]
        public async Task RouteHandler_RemovesRouteGroupFromRouteValues()
        {
            // Arrange
            var invoker = new Mock<IActionInvoker>();
            invoker
                .Setup(i => i.InvokeAsync())
                .Returns(Task.FromResult(true));

            var invokerFactory = new Mock<IActionInvokerFactory>();
            invokerFactory
                .Setup(f => f.CreateInvoker(It.IsAny<ActionContext>()))
                .Returns<ActionContext>((c) =>
                {
                    return invoker.Object;
                });

            var context = CreateRouteContext();
            var handler = CreateMvcRouteHandler(invokerFactory: invokerFactory.Object);

            var originalRouteData = context.RouteData;
            originalRouteData.Values.Add(TreeRouter.RouteGroupKey, "/Home/Test");

            // Act
            await handler.RouteAsync(context);

            // Assert
            Assert.Same(originalRouteData, context.RouteData);


            Assert.False(context.RouteData.Values.ContainsKey(TreeRouter.RouteGroupKey));
        }

        [Fact]
        public async Task RouteHandler_WritesDiagnostic_ActionSelected()
        {
            // Arrange
            var listener = new TestDiagnosticListener();

            var context = CreateRouteContext();
            context.RouteData.Values.Add("tag", "value");

            var handler = CreateMvcRouteHandler(diagnosticListener: listener);
            await handler.RouteAsync(context);

            // Act
            await context.Handler(context.HttpContext);

            // Assert
            Assert.NotNull(listener.BeforeAction?.ActionDescriptor);
            Assert.NotNull(listener.BeforeAction?.HttpContext);

            var routeValues = listener.BeforeAction?.RouteData?.Values;
            Assert.NotNull(routeValues);

            Assert.Equal(1, routeValues.Count);
            Assert.Contains(routeValues, kvp => kvp.Key == "tag" && string.Equals(kvp.Value, "value"));
        }

        [Fact]
        public async Task RouteHandler_WritesDiagnostic_ActionInvoked()
        {
            // Arrange
            var listener = new TestDiagnosticListener();

            var context = CreateRouteContext();

            var handler = CreateMvcRouteHandler(diagnosticListener: listener);
            await handler.RouteAsync(context);

            // Act
            await context.Handler(context.HttpContext);

            // Assert
            Assert.NotNull(listener.AfterAction?.ActionDescriptor);
            Assert.NotNull(listener.AfterAction?.HttpContext);
        }

        private MvcRouteHandler CreateMvcRouteHandler(
            ActionDescriptor actionDescriptor = null,
            IActionSelector actionSelector = null,
            IActionInvokerFactory invokerFactory = null,
            ILoggerFactory loggerFactory = null,
            object diagnosticListener = null)
        {
            var actionContextAccessor = new ActionContextAccessor();

            if (actionDescriptor == null)
            {
                var mockAction = new Mock<ActionDescriptor>();
                actionDescriptor = mockAction.Object;
            }

            if (actionSelector == null)
            {
                var mockActionSelector = new Mock<IActionSelector>();
                mockActionSelector.Setup(a => a.Select(It.IsAny<RouteContext>()))
                    .Returns(actionDescriptor);
                actionSelector = mockActionSelector.Object;
            }

            if (loggerFactory == null)
            {
                loggerFactory = NullLoggerFactory.Instance;
            }

            var diagnosticSource = new DiagnosticListener("Microsoft.AspNetCore");
            if (diagnosticListener != null)
            {
                diagnosticSource.SubscribeWithAdapter(diagnosticListener);
            }

            if (invokerFactory == null)
            {
                var mockInvoker = new Mock<IActionInvoker>();
                mockInvoker.Setup(i => i.InvokeAsync())
                    .Returns(Task.FromResult(true));

                var mockInvokerFactory = new Mock<IActionInvokerFactory>();
                mockInvokerFactory.Setup(f => f.CreateInvoker(It.IsAny<ActionContext>()))
                    .Returns(mockInvoker.Object);

                invokerFactory = mockInvokerFactory.Object;
            }

            return new MvcRouteHandler(
                invokerFactory,
                actionSelector,
                diagnosticSource,
                loggerFactory,
                actionContextAccessor);
        }

        private RouteContext CreateRouteContext()
        {
            var routingFeature = new RoutingFeature();

            var httpContext = new Mock<HttpContext>();
            httpContext
                .Setup(h => h.Features[typeof(IRoutingFeature)])
                .Returns(routingFeature);

            var routeContext = new RouteContext(httpContext.Object);
            routingFeature.RouteData = routeContext.RouteData;
            return routeContext;
        }

        private class RoutingFeature : IRoutingFeature
        {
            public RouteData RouteData { get; set; }
        }
    }
}