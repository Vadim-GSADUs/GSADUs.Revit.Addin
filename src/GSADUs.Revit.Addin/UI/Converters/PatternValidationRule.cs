using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GSADUs.Revit.Addin.UI
{
    /// <summary>
    /// WPF ValidationRule that validates filename/pattern fields contain required placeholder tokens.
    /// - Fails when the input is null/empty/whitespace.
    /// - Fails when any of the RequiredTokens are missing from the input.
    ///
    /// Example usage (static tokens list):
    ///
    ///   <TextBox Width="300">
    ///     <TextBox.Text>
    ///       <Binding Path="PdfPattern" Mode="TwoWay" UpdateSourceTrigger="PropertyChanged" NotifyOnValidationError="True">
    ///         <Binding.ValidationRules>
    ///           <ui:PatternValidationRule>
    ///             <ui:PatternValidationRule.RequiredTokens>
    ///               <x:Array Type="{x:Type sys:String}">
    ///                 <sys:String>{SetName}</sys:String>
    ///               </x:Array>
    ///             </ui:PatternValidationRule.RequiredTokens>
    ///           </ui:PatternValidationRule>
    ///         </Binding.ValidationRules>
    ///       </Binding>
    ///     </TextBox.Text>
    ///   </TextBox>
    ///
    /// Bindable tokens via proxy (when tokens must come from a bound VM collection):
    ///   <ui:PatternValidationRule>
    ///     <ui:PatternValidationRule.TokensProxy>
    ///       <ui:BindingProxy Data="{Binding RequiredTokensFromVm}" />
    ///     </ui:PatternValidationRule.TokensProxy>
    ///   </ui:PatternValidationRule>
    /// </summary>
    internal sealed class PatternValidationRule : ValidationRule
    {
        /// <summary>
        /// Optional CLR list settable in XAML for static tokens.
        /// </summary>
        public IEnumerable<string>? RequiredTokens { get; set; }

        /// <summary>
        /// Optional proxy enabling binding tokens from a VM.
        /// </summary>
        public BindingProxy? TokensProxy { get; set; }

        /// <summary>
        /// Case sensitivity for token matching. Default Ordinal (case-sensitive).
        /// </summary>
        public StringComparison Comparison { get; set; } = StringComparison.Ordinal;

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            try
            {
                var s = (value as string)?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(s))
                {
                    return new ValidationResult(false, "Pattern is required");
                }

                var tokens = GetEffectiveTokens();
                foreach (var raw in tokens)
                {
                    var t = raw ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(t)) continue;
                    if (s.IndexOf(t, Comparison) < 0)
                    {
                        return new ValidationResult(false, $"Missing token {t}");
                    }
                }

                return ValidationResult.ValidResult;
            }
            catch (Exception ex)
            {
                return new ValidationResult(false, ex.Message);
            }
        }

        private IEnumerable<string> GetEffectiveTokens()
        {
            if (TokensProxy?.Data is IEnumerable<string> bound) return bound;
            if (RequiredTokens != null) return RequiredTokens;
            return Enumerable.Empty<string>();
        }
    }

    /// <summary>
    /// Helper proxy that enables data binding within ValidationRules.
    /// Use as an inline element to bind complex data (e.g., a VM collection) into the rule.
    /// </summary>
    internal sealed class BindingProxy : Freezable
    {
        public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
            nameof(Data), typeof(object), typeof(BindingProxy), new PropertyMetadata(null));

        public object? Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        protected override Freezable CreateInstanceCore() => new BindingProxy();
    }
}
