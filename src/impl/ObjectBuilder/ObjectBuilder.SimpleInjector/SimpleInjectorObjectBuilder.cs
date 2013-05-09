using System;
using System.Collections.Generic;

namespace NServiceBus.ObjectBuilder.SimpleInjector
{
	using System.Collections;
	using System.Linq;
	using System.Linq.Expressions;
	using Common;
	using global::SimpleInjector;
	using global::SimpleInjector.Extensions;
	using global::SimpleInjector.Extensions.LifetimeScoping;

	/// <summary>
	/// ObjectBuilder implementation for the SimpleInjector IoC-Container
	/// </summary>
	public class SimpleInjectorObjectBuilder : IContainer
	{
		private static readonly Lifestyle LifetimeScopeLifestyle = new LifetimeScopeLifestyle();

		private readonly Container container;
		private LifetimeScope scope;

		///<summary>
		/// Instantites the class with an empty SimpleInjector container.
		///</summary>
		public SimpleInjectorObjectBuilder()
			: this(new Container())
		{
		}

		///<summary>
		/// Instantiates the class utilizing the given LifetimeScope.
		///</summary>
		///<param name="scope"></param>
		public SimpleInjectorObjectBuilder(Container container)
		{
			if (container == null) throw new ArgumentNullException("container");

			this.container = container;

			this.container.EnableLifetimeScoping();

			this.AutoResolveCollectionsWithNServiceBusTypes();
		}

		private SimpleInjectorObjectBuilder(Container container, LifetimeScope scope)
		{
			this.container = container;
			this.scope = scope;
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		/// <summary>
		/// Disposes the container and all resources instantiated by the container.
		/// </summary>
		protected virtual void Dispose(bool disposing)
		{
			if (disposing && scope != null)
			{
				scope.Dispose();
				scope = null;
			}
		}

		///<summary>
		/// Build an instance of a given type.
		///</summary>
		///<param name="typeToBuild"></param>
		///<returns></returns>
		public object Build(Type typeToBuild)
		{
			return container.GetInstance(typeToBuild);
		}

		public IContainer BuildChildContainer()
		{
			return new SimpleInjectorObjectBuilder(this.container, this.container.BeginLifetimeScope());
		}

		///<summary>
		/// Build all instances of a given type.
		///</summary>
		///<param name="typeToBuild"></param>
		///<returns></returns>
		public IEnumerable<object> BuildAll(Type typeToBuild)
		{
			return this.container.GetAllInstances(typeToBuild);
		}

		public void Configure(Type component, DependencyLifecycle dependencyLifecycle)
		{
			this.container.Register(component, component, ToLifestyle(dependencyLifecycle));
		}

		public void Configure<T>(Func<T> component, DependencyLifecycle dependencyLifecycle)
		{
			// https://simpleinjector.codeplex.com/discussions/441823
			var registration =
				ToLifestyle(dependencyLifecycle)
					.CreateRegistration(typeof(T), () => component(), container);

			container.AddRegistration(typeof(T), registration);
		}

		public void ConfigureProperty(Type component, string property, object value)
		{
			var prop = component.GetProperty(property);

			if (value == null) throw new ArgumentNullException("value");
			if (prop == null) throw new ArgumentException("property '" + property + "' not found");
			if (!prop.PropertyType.IsInstanceOfType(value))
				throw new ArgumentException("value type is incorrect", "value");

			var actionType = typeof(Action<>).MakeGenericType(component);

			var parameter = Expression.Parameter(component);

			var action = Expression.Lambda(actionType,
				Expression.Assign(
					Expression.Property(parameter, property),
					Expression.Constant(value)),
				parameter)
				.Compile();

			var initializer = typeof(Container).GetMethod("RegisterInitializer")
				.MakeGenericMethod(component);

			initializer.Invoke(container, new[] { action });
		}

		///<summary>
		/// Register a singleton instance of a dependency.
		///</summary>
		///<param name="lookupType"></param>
		///<param name="instance"></param>
		public void RegisterSingleton(Type lookupType, object instance)
		{
			container.RegisterSingle(lookupType, instance);
		}

		public bool HasComponent(Type componentType)
		{
			return container.GetRegistration(componentType) != null;
		}

		private Lifestyle ToLifestyle(DependencyLifecycle dependencyLifecycle)
		{
			switch (dependencyLifecycle)
			{
				case DependencyLifecycle.InstancePerCall:
					return Lifestyle.Transient;
				case DependencyLifecycle.SingleInstance:
					return Lifestyle.Singleton;
				case DependencyLifecycle.InstancePerUnitOfWork:
					return LifetimeScopeLifestyle;
				default:
					throw new ArgumentException("Unhandled lifecycle - " + dependencyLifecycle);
			}
		}

		private void AutoResolveCollectionsWithNServiceBusTypes()
		{
			// This event gets triggered when an unregistered type gets resolved.
			this.container.ResolveUnregisteredType += (s, e) =>
			{
				// We only handle IEnumerable<T>
				if (e.UnregisteredServiceType.IsGenericType &&
					e.UnregisteredServiceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
				{
					Type elementType = e.UnregisteredServiceType.GetGenericArguments().Single();

					// We only handle types T that are defined in an NServiceBus interface.
					if (elementType.Namespace.StartsWith("NServiceBus"))
					{
						this.AddCollectionRegistration(e, elementType);
					}
				}
			};
		}

		private void AddCollectionRegistration(UnregisteredTypeEventArgs e, Type elementType)
		{
			// Find all registrations in the container that implement or inherit from the given elementType.
			// Calling ToArray() ensures this query is ran just once (crusial for performance).
			var registrations = (
				from registration in container.GetCurrentRegistrations()
				where elementType.IsAssignableFrom(registration.ServiceType)
				select registration)
				.ToArray();

			if (registrations.Any())
			{
				// Here we map the array of registrations to an enumerable of service instances (that are created
				// dynamically. We don't call ToArray here; this way the lifestyle is preserved.
				IEnumerable<object> collection =
					registrations.Select(registration => registration.GetInstance());

				// Register the collection in the container.
				e.Register(Expression.Constant(CastCollection(collection, elementType)));
			}
		}

		private static IEnumerable CastCollection(IEnumerable<object> collection, Type elementType)
		{
			var castMethod = typeof(Enumerable).GetMethod("Cast").MakeGenericMethod(elementType);

			return (IEnumerable)castMethod.Invoke(null, new object[] { collection });
		}
	}
}