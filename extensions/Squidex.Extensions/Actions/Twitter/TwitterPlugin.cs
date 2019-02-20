﻿// ==========================================================================
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex UG (haftungsbeschraenkt)
//  All rights reserved. Licensed under the MIT license.
// ==========================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Squidex.Domain.Apps.Core.HandleRules;
using Squidex.Infrastructure.Plugins;

namespace Squidex.Extensions.Actions.Twitter
{
    public sealed class TwitterPlugin : IPlugin
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<TwitterOptions>(
                configuration.GetSection("twitter"));

            RuleActionRegistry.Add<TweetAction>();
        }
    }
}
