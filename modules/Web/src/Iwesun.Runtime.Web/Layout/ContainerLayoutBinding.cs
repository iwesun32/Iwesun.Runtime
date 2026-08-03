namespace Iwesun.Runtime.Web;

public sealed record ContainerLayoutBinding
{
	public ContainerLayoutMechanism Mechanism { get; }
	public LayoutContainerReference Container { get; }
	public ContainerLayoutConstraint Constraint { get; }
	public LayoutPhysicalAxis? FlexMainAxis { get; }
	public string SourceDescription { get; }

	public ContainerLayoutBinding(
		ContainerLayoutMechanism mechanism,
		LayoutContainerReference container,
		ContainerLayoutConstraint constraint,
		string sourceDescription,
		LayoutPhysicalAxis? flexMainAxis = null)
	{
		ArgumentNullException.ThrowIfNull(constraint);
		ArgumentException.ThrowIfNullOrWhiteSpace(sourceDescription);
		ContainerLayoutBindingValidator.Validate(
			mechanism,
			container,
			constraint,
			flexMainAxis);
		Mechanism = mechanism;
		Container = container;
		Constraint = constraint;
		FlexMainAxis = flexMainAxis;
		SourceDescription = sourceDescription;
	}
}
