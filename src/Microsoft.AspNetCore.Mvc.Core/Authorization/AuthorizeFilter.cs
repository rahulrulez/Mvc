// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.Mvc.Authorization
{
    /// <summary>
    /// An implementation of <see cref="IAsyncAuthorizationFilter"/> which applies a specific
    /// <see cref="AuthorizationPolicy"/>. MVC recognizes the <see cref="AuthorizeAttribute"/> and adds an instance of
    /// this filter to the associated action or controller.
    /// </summary>
    public class AuthorizeFilter : IAsyncAuthorizationFilter
    {
        /// <summary>
        /// Initialize a new <see cref="AuthorizeFilter"/> instance.
        /// </summary>
        /// <param name="policy">Authorization policy to be used.</param>
        public AuthorizeFilter(AuthorizationPolicy policy)
        {
            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            Policy = policy;
        }

        /// <summary>
        /// Gets the authorization policy to be used.
        /// </summary>
        public AuthorizationPolicy Policy { get; }

        /// <inheritdoc />
        public virtual async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // Build a ClaimsPrincipal with the Policy's required authentication types
            if (Policy.AuthenticationSchemes != null && Policy.AuthenticationSchemes.Any())
            {
                ClaimsPrincipal newPrincipal = null;
                foreach (var scheme in Policy.AuthenticationSchemes)
                {
                    var result = await context.HttpContext.Authentication.AuthenticateAsync(scheme);
                    if (result != null)
                    {
                        newPrincipal = SecurityHelper.MergeUserPrincipal(newPrincipal, result);
                    }
                }
                // If all schemes failed authentication, provide a default identity anyways
                if (newPrincipal == null)
                {
                    newPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
                }
                context.HttpContext.User = newPrincipal;
            }

            // Allow Anonymous skips all authorization
            if (context.Filters.Any(item => item is IAllowAnonymousFilter))
            {
                return;
            }

            var httpContext = context.HttpContext;
            var authService = httpContext.RequestServices.GetRequiredService<IAuthorizationService>();

            // Note: Default Anonymous User is new ClaimsPrincipal(new ClaimsIdentity())
            if (!await authService.AuthorizeAsync(httpContext.User, context, Policy))
            {
                context.Result = new ChallengeResult(Policy.AuthenticationSchemes.ToArray());
            }
        }
    }
}
