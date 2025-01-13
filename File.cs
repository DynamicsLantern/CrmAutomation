namespace Extensions;
public static class SeleniumExtensions
{
	public static readonly TimeSpan DefaultTimeout = new(0, 0, 30);
	public const int DefaultRetryAttempts = 5;

	[DebuggerNonUserCode]
	public static bool WaitUntil(this IWebDriver driver,
		Predicate<IWebDriver> predicate,
		TimeSpan? timeout = null,
		Action successCallback = null,
		Action failureCallback = null)
	{
		var wait = new DefaultWait<IWebDriver>(driver) { Timeout = timeout ?? DefaultTimeout };
		wait.IgnoreExceptionTypes(typeof(NoSuchElementException), typeof(StaleElementReferenceException));

		bool success = false;
		try
		{
			success = wait.Until(d => predicate(d));
		}
		catch (WebDriverTimeoutException) { }

		if (success)
			successCallback?.Invoke();
		else
			failureCallback?.Invoke();

		return success;
	}

	[DebuggerNonUserCode]
	public static IWebElement WaitUntil(this ISearchContext driver, Func<ISearchContext, IWebElement> searchFunc,
		TimeSpan? timeout = null,
		Action<IWebElement> successCallback = null, Action failureCallback = null)
	{
		var wait = new DefaultWait<ISearchContext>(driver)
		{
			Timeout = timeout ?? DefaultTimeout
		};
		wait.IgnoreExceptionTypes(typeof(NoSuchElementException), typeof(StaleElementReferenceException));

		bool success = false;
		IWebElement element = null;
		try
		{
			element = wait.Until(searchFunc);
			success = element != null;
		}
		catch (WebDriverTimeoutException)
		{
		}

		if (success)
			successCallback?.Invoke(element);
		else
			failureCallback?.Invoke();

		return element;
	}

	[DebuggerNonUserCode]
	public static object ExecuteScript(this IWebDriver driver, string script, params object[] args)
	{
		var scriptExecutor = driver as IJavaScriptExecutor ?? throw new InvalidOperationException(
				$"The driver type '{driver.GetType().FullName}' does not support Javascript execution.");
		return scriptExecutor.ExecuteScript(script, args);
	}

	[DebuggerNonUserCode]
	public static bool RepeatUntil(this IWebDriver driver,
		Action action,
		Predicate<IWebDriver> predicate,
		TimeSpan? timeout = null,
		int attemps = DefaultRetryAttempts,
		Action successCallback = null,
		Action failureCallback = null)
	{
		timeout = timeout ?? DefaultTimeout;
		var waittime = new TimeSpan(timeout.Value.Ticks / attemps);

		WebDriverWait wait = new WebDriverWait(driver, waittime);
		wait.IgnoreExceptionTypes(typeof(NoSuchElementException), typeof(StaleElementReferenceException));

		bool success = predicate(driver);
		while (!success && attemps > 0)
		{
			try
			{
				action();
				attemps--;
				success = wait.Until(d => predicate(d));
			}
			catch (WebDriverTimeoutException)
			{
			}
		}

		if (success)
			successCallback?.Invoke();
		else
			failureCallback?.Invoke();

		return success;
	}

	[DebuggerNonUserCode]
	public static IWebElement WaitUntilVisible(this ISearchContext driver, By by,
		TimeSpan? timeout = null,
		Action<IWebElement> successCallback = null,
		Action failureCallback = null)
	{
		return WaitUntil(driver, d => d.FindVisible(by));
	}

	[DebuggerNonUserCode]
	public static IWebElement FindVisible(this ISearchContext driver, By locator)
	{
		ReadOnlyCollection<IWebElement> elements = driver.FindElements(locator);
		IWebElement result = elements.FirstOrDefault(x => x?.Displayed == true);
		return result;
	}
}

	public static class CrmWebDriverExtenstions
	{

		#region FIELD GETTERS

		public static string GetText(this IWebDriver driver, string fieldId)
		{
			// Make sure the field is ready
			var fieldExist = driver.WaitUntil(x => x.FieldExists(fieldId));

			if (!fieldExist)
			{
				throw new NotFoundException($"Field '{fieldId}' was not found");
			}

			// Check what type of control it is
			var controlType = driver.GetControlType(fieldId);
			return controlType switch
			{
				"lookup" => driver.GetLookupText(fieldId),
				"optionset" => driver.GetOptionSetText(fieldId),
				"boolean" => driver.GetBooleanText(fieldId),
				"memo" or "string" or "money" or "integer" => driver.GetStandardText(fieldId),
				"datetime" => driver.GetDateTimeText(fieldId),
				_ => throw new NotImplementedException($"Control type '{controlType}' has not been implemented"),
			};
		}

		private static string GetOptionSetText(this IWebDriver driver, string fieldId)
		{
			return driver.ExecuteJavaScript<string>($"return Xrm.Page.getAttribute(\"{fieldId}\").getText();");
		}

		private static string GetBooleanText(this IWebDriver driver, string fieldId)
		{
			var val = driver.ExecuteJavaScript<bool?>($"return Xrm.Page.getAttribute(\"{fieldId}\").getValue();");
			return (val.HasValue && val.Value) ? "Yes" : "No";
		}

		private static string GetLookupText(this IWebDriver driver, string fieldId)
		{
			if (driver.ExecuteJavaScript<bool>($"return Xrm.Page.getAttribute(\"{fieldId}\").getValue() == null;"))
				return null;

			return driver.ExecuteJavaScript<string>($"return Xrm.Page.getAttribute(\"{fieldId}\").getValue()[0].name;");
		}

		private static string GetStandardText(this IWebDriver driver, string fieldId)
		{
			var objResult = driver.ExecuteScript($"return Xrm.Page.getAttribute(\"{fieldId}\").getValue();");
			return objResult?.ToString();
		}

		private static string GetDateTimeText(this IWebDriver driver, string fieldId)
		{
			var rawVal = driver.ExecuteScript($"if (Xrm.Page.getAttribute(\"{fieldId}\").getValue()) " +
											  $"{{ return Xrm.Page.getAttribute(\"{fieldId}\").getValue().toUTCString(); }} " +
											  $"else {{ return null; }}");

			return (DateTime.Parse(rawVal?.ToString()) as DateTime?)?.ToShortDateString();
		}

		#endregion

		#region FIELD SETTERS
		public static void SetText(this IWebDriver driver, string fieldId, string value)
		{
			// Make sure the field exists
			var fieldExist = driver.WaitUntil(x => x.FieldExists(fieldId));

			if (!fieldExist)
			{
				throw new NotFoundException($"Field '{fieldId}' was not found");
			}

			// Make sure it's not locked (read only)
			var isLocked = driver.WaitUntil(x => x.IsFieldDisabled(fieldId));
			if (isLocked)
			{
				throw new Exception($"Field '{fieldId}' is currently disabled");
			}

			//, $"Field \"{fieldId}\" is currently disabled");

			// Check what type of control it is
			var controlType = driver.GetControlType(fieldId);
			switch (controlType)
			{
				case "lookup":
					driver.SetLookupText(fieldId, value);
					break;
				case "optionset":
					driver.SetOptionSetText(fieldId, value);
					break;
				case "boolean":
					driver.SetBooleanValue(fieldId, value.ToLower() == "true");
					break;
				case "memo":
				case "string":
					driver.SetStandardText(fieldId, value);
					break;
				case "money":
				case "integer":
				case "double":
					driver.SetNumber(fieldId, value);
					break;
				case "datetime":
					driver.SetDateText(fieldId, value);
					break;
				default:
					throw new NotImplementedException($"Control type '{controlType}' has not been implemented");
			}

			// This ensures things like validation and show/hide triggers get executed
			driver.ExecuteScript($"return Xrm.Page.getAttribute(\"{fieldId}\").fireOnChange();");
		}

		public static void SetText(this IWebDriver driver, string fieldId, int value)
		{
			driver.SetText(fieldId, value.ToString());
		}

		public static void SetText(this IWebDriver driver, string fieldId, DateTime? dateTime)
		{
			if (!dateTime.HasValue)
			{
				driver.SetText(fieldId, "");
				return;
			}
			driver.SetText(fieldId, dateTime.Value.ToShortDateString());
		}

		public static string SelectFirstLookupValue(this IWebDriver driver, string fieldId)
		{
			driver.SetLookupText(fieldId, "");
			Thread.Sleep(1500);
			return driver.GetText(fieldId);
		}

		public static void Click(this IWebDriver driver, string fieldId)
		{
			if (driver.GetControlType(fieldId) == "lookup")
				driver.FindElement(By.CssSelector($"[data-id={fieldId}] li")).Click();
			else
				driver.FindElement(By.CssSelector($"[data-id={fieldId}]")).Click();
		}

		private static void SetLookupText(this IWebDriver driver, string fieldId, string searchValue)
		{
			driver.RepeatUntil(() =>
			{
				driver.ClearLookup(fieldId);
				Thread.Sleep(1000);

				// Click the field to display the search input box
				driver.FindElement(By.CssSelector($"[data-id={fieldId}]")).Click();
				Thread.Sleep(1000);

				// Input box should appear
				driver.WaitUntilVisible(By.CssSelector($"[data-id={fieldId}] input[role=combobox]"));

				// Enter the search text
				var searchBox = driver.FindElement(By.CssSelector($"[data-id={fieldId}] input[role=combobox]"));
				searchBox.Clear();
				searchBox.SendKeys(string.Empty);
				searchBox.SendKeys(searchValue);

				// Click search icon
				driver.FindElement(By.CssSelector($"[data-id={fieldId}] button[aria-label^=Search]")).Click();

				// Autocomplete dialog should appear
				driver.WaitUntilVisible(By.CssSelector("ul[role=tree][id*=LookupResultsDropdown]"));

				// Select the first result
				driver.FindElement(By.CssSelector("ul[role=tree][id*=LookupResultsDropdown]"))
					.FindElements(By.CssSelector("li[role=treeitem][id*=LookupResultsDropdown]")).First().Click();
			},
			x => x.GetText(fieldId) == searchValue);
		}

		public static void ClearLookup(this IWebDriver driver, string lookupFieldId)
		{
			driver.ExecuteScript($"Xrm.Page.getAttribute(\"{lookupFieldId}\").setValue(\"\");");
		}

		private static void SetBooleanValue(this IWebDriver driver, string fieldId, bool value)
		{
			driver.ExecuteScript($"return Xrm.Page.getAttribute(\"{fieldId}\").setValue({value.ToString().ToLower()});");
		}

		private static void SetOptionSetText(this IWebDriver driver, string fieldId, string value)
		{
			var objAllOptions = driver.ExecuteJavaScript<ReadOnlyCollection<object>>($"return Xrm.Page.getAttribute(\"{fieldId}\").getOptions();");
			var matchingOption = objAllOptions.Cast<Dictionary<string, object>>().FirstOrDefault(option => option["text"]?.ToString() == value);

			if (matchingOption != null)
			{
				var valueSet = driver.RepeatUntil(() =>
				{
					driver.ExecuteScript($"return Xrm.Page.getAttribute(\"{fieldId}\").setValue({matchingOption["value"]});");
				},
				x => x.GetText(fieldId) == value);

				if (!valueSet)
				{
					throw new Exception($"Failed to set option set value to '{value}'. Actual value was '{driver.GetText(fieldId)}'");
				}
			}
			else
			{
				throw new IndexOutOfRangeException($"Value '{value}' not found in option set '{fieldId}'");
			}
		}

		private static void SetStandardText(this IWebDriver driver, string fieldId, string value)
		{
			driver.ExecuteScript($"Xrm.Page.getAttribute(\"{fieldId}\").setValue(\"{value}\");");
		}

		private static void SetNumber(this IWebDriver driver, string fieldId, string value)
		{
			driver.ExecuteScript($"Xrm.Page.getAttribute(\"{fieldId}\").setValue({value});");
		}

		private static void SetDateText(this IWebDriver driver, string fieldId, string value)
		{
			// Returns the number of milliseconds since Unix epoch (January 1, 1970)
			long DateTimeMinTimeTicks = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

			long ToJavaScriptMilliseconds(DateTime dt)
			{
				return (dt.ToUniversalTime().Ticks - DateTimeMinTimeTicks) / 10000;
			}

			if (string.IsNullOrWhiteSpace(value))
			{
				driver.ExecuteScript($"Xrm.Page.getAttribute('{fieldId}').setValue('')");
			}
			else
			{
				var dateValue = ToJavaScriptMilliseconds(DateTime.Parse(value));
				driver.ExecuteScript($"Xrm.Page.getAttribute('{fieldId}').setValue(new Date({dateValue}));");
			}
		}



		#endregion

		#region FIELD PROPERTIES

		public static bool FieldExists(this IWebDriver driver, string fieldId)
		{
			return driver.ExecuteJavaScript<bool>($"return (Xrm.Page.getAttribute(\"{fieldId}\") != null);");
		}

		public static bool IsFieldDisabled(this IWebDriver driver, string fieldId)
		{
			return driver.ExecuteJavaScript<bool>($"return Xrm.Page.getControl(\"{fieldId}\").getDisabled();");
		}

		public static bool IsFieldVisible(this IWebDriver driver, string fieldId)
		{
			return driver.FieldExists(fieldId) && driver.ExecuteJavaScript<bool>($"return Xrm.Page.getControl(\"{fieldId}\").getVisible();");
		}

		public static bool IsFieldInteractable(this IWebDriver driver, string fieldId)
		{
			return driver.FieldExists(fieldId) && driver.IsFieldVisible(fieldId) && !driver.IsFieldDisabled(fieldId);
		}

		public static bool IsFieldEmpty(this IWebDriver driver, string fieldId)
		{
			return string.IsNullOrWhiteSpace(driver.GetText(fieldId));
		}

		public static string GetControlType(this IWebDriver driver, string fieldId)
		{
			return driver.ExecuteJavaScript<string>($"return Xrm.Page.getAttribute(\"{fieldId}\").getAttributeType();");
		}

		#endregion
	}
