namespace NServiceBus.ObjectBuilder.SimpleInjector
{
	using System;
	using System.Reflection;
	using global::SimpleInjector;
	using global::SimpleInjector.Advanced;

	public class PropertyInjector : IPropertySelectionBehavior
	{
		readonly Func<Type, bool> hasComponent;

		public PropertyInjector(Func<Type, bool> hasComponent)
		{
			this.hasComponent = hasComponent;
		}

		public bool SelectProperty(Type serviceType, PropertyInfo propertyInfo)
		{
			return hasComponent(propertyInfo.PropertyType);
		}
	}
}