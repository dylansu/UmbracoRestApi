﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Cors;
using System.Web.Http.Dispatcher;
using System.Web.Http.Routing;
using System.Web.Routing;

namespace Umbraco.RestApi
{
    internal static class RouteExtensions
    {
        /// <summary>
        /// This will create attribute routes for specific controllers without interfering with normal attribute routing
        /// </summary>
        /// <param name="originalConfig"></param>
        /// <param name="routeNamePrefix">
        /// For non-named attributed routes, they are created as one master route containing sub-routes, this will be the 
        /// prefix name for that route, the suffix will be the controller type.
        /// For named attribute routes, those names will be used.
        /// </param>
        /// <param name="controllerTypes">
        /// Route these controllers based on their attributes. They will be routed in order (precedence)
        /// </param>
        /// <param name="routeCallback"></param>
        /// <param name="inheritedAttributes"></param>
        public static void MapControllerAttributeRoutes(
            this HttpConfiguration originalConfig, 
            string routeNamePrefix,
            IEnumerable<Type> controllerTypes,
            Action<IHttpRoute> routeCallback = null,
            bool inheritedAttributes = false)
        {
            foreach (var controllerType in controllerTypes)
            {
                //new temp config to clone
                var tempConfig = new HttpConfiguration();

                tempConfig.Services.Replace(typeof(IHttpControllerTypeResolver), new SpecificControllerTypeResolver(new[] { controllerType }));
                tempConfig.MapHttpAttributeRoutes(new CustomRouteAttributeDirectRouteProvider(inheritedAttributes));

                var isInitialized = false;

                var originalInit = originalConfig.Initializer;
                var controllerTypeLocal = controllerType;

                originalConfig.Initializer = configuration =>
                {
                    //Track a boolean because otherwise we'll end up in an infinite loop because
                    // of the ctor of the HttpControllerDescriptor below will clone http config
                    // per controller and invoke the original initializer, and we don't want to
                    // initialize the clones again with the attribute routes.
                    if (!isInitialized)
                    {
                        isInitialized = true;

                        tempConfig.EnsureInitialized();

                        var controllerDescriptors = new List<HttpControllerDescriptor>();

                        //get the routes created
                        foreach (var attributeRoute in tempConfig.Routes)
                        {
                            //attributeRoute.Handler = GetMessageHandler()

                            //in many cases it's a collection of routes contained in a single route
                            var routeCollection = attributeRoute as IReadOnlyCollection<IHttpRoute>;
                            var routeName = string.Format("{0}{1}", routeNamePrefix, controllerTypeLocal.FullName);
                            if (routeCollection != null)
                            {
                                //update each attribute route's action descriptor http configuration property
                                //to be the real configuration 
                                foreach (var httpRoute in routeCollection)
                                {
                                    SetDescriptorsOnRoute(httpRoute, configuration, controllerDescriptors);
                                }
                            }
                            else
                            {
                                SetDescriptorsOnRoute(attributeRoute, configuration, controllerDescriptors);
                                //get the route name from the data tokens, since this is a named route
                                routeName = attributeRoute.DataTokens["Umb_RouteName"].ToString();
                            }

                            //callback can modify the route
                            if (routeCallback != null)
                            {
                                routeCallback(attributeRoute);
                            }

                            //now we need to add the route back to the main configuration
                            originalConfig.Routes.Add(
                                routeName,
                                attributeRoute);
                        }
                    }

                    originalInit(configuration);
                };
            }
        }

        private static void SetDescriptorsOnRoute(IHttpRoute route, HttpConfiguration configuration, ICollection<HttpControllerDescriptor> controllerDescriptors)
        {
            var actionDescriptors = route.DataTokens["actions"] as IEnumerable<HttpActionDescriptor>;
            if (actionDescriptors != null)
            {
                foreach (var descriptor in actionDescriptors)
                {
                    descriptor.Configuration = configuration;

                    //IMPORTANT: We are making a new instance of a HttpControllerDescriptor to force
                    // the descriptor to initialize again so that IControllerConfiguration executes.
                    // if they are not the same webapi will throw an exception about ambiguous controllers.

                    //for any controller type, we need to have the exact same controller descriptor instance

                    var found = controllerDescriptors.SingleOrDefault(x => x.ControllerType == descriptor.ControllerDescriptor.ControllerType);
                    if (found == null)
                    {
                        found = new HttpControllerDescriptor(
                            configuration,
                            descriptor.ControllerDescriptor.ControllerName,
                            descriptor.ControllerDescriptor.ControllerType);
                        controllerDescriptors.Add(found);
                    }

                    descriptor.ControllerDescriptor = found;
                }
            }
        }

        /// <summary>
        /// Gets a custom message handler that explicitly contains the CorsMessageHandler for use 
        /// with our RestApi routes.
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        private static HttpMessageHandler GetMessageHandler(HttpConfiguration config)
        {
            // Create a message handler chain with an end-point.
            return HttpClientFactory.CreatePipeline(
                new HttpControllerDispatcher(config), 
                new DelegatingHandler[]
                {                   
                    //Explicitly include the CorsMessage handler!
                    // we're doing this so people don't have to do EnableCors() in their startup,
                    // we don't care about that, we always want to support Cors for the rest api
                    new CorsMessageHandler(config)
                });
        }



    }

    

}