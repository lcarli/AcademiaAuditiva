public class FilterOptionGroup
{
	public string Label { get; set; }
	public string Name { get; set; }
	public List<FilterOption> Options { get; set; }
}

public class FilterOption
{
	public string Value { get; set; }
	public string Text { get; set; }

	public FilterOption() { }

	public FilterOption(string value, string text)
	{
		Value = value;
		Text = text;
	}
}
