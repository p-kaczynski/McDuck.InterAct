using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;

namespace McDuck.InterAct
{
    public class InteractionBuilder : InteractionBuilder.IBuilderBase, InteractionBuilder.IBuilderMenu, InteractionBuilder.IInteractionContinuation, InteractionBuilder.IInteraction
    {
        private Dictionary<string, string> ParentInputs { get; }

        private TextReader In { get; set; }
        private TextWriter Out { get; set; }

        private string Intro { get; set; }

        private Dictionary<string, string> InputPrompts { get; } = new Dictionary<string, string>();
        private Dictionary<string, string> Inputs { get; } = new Dictionary<string, string>();


        private List<KeyValuePair<string, IInteraction>> MenuInteractions { get; } = new List<KeyValuePair<string, IInteraction>>();

        private string Prompt { get; set; }
        private Dictionary<string, IInteraction> PromptInteractions { get; set; }

        private Action<TextReader, TextWriter, Dictionary<string, string>> Action { get; set; }

        private bool ExitAfterAction { get; set; }

        private InteractionBuilder(Dictionary<string, string> parentInputs = null)
        {
            ParentInputs = parentInputs;
        }

        [PublicAPI]
        public static IBuilderBase Create(TextReader input = null, TextWriter output = null) => new InteractionBuilder
        {
            In = input ?? Console.In,
            Out = output ?? Console.Out
        };

        private IBuilderBase CreateChild() => new InteractionBuilder(Inputs) {In = In, Out = Out};

        [PublicAPI]
        public interface IBuilderBase : IInteractionFinisher
        {
            IBuilderBase WithIntro(string text);
            IInteractionFinisher WithMenu(Action<IBuilderMenu> menuBuilderConfig);

            IBuilderBase PromptForInput(string inputKey, string prompt);
            IInteractionFinisher Prompt(string prompt, params (string answer, Func<IBuilderBase, IInteraction> config)[] options);
            IInteractionFinisher PromptCaseInsensitive(string prompt, params (string answer, Func<IBuilderBase, IInteraction> config)[] options);
        }

        IBuilderBase IBuilderBase.WithIntro(string text)
        {
            Intro = text;
            return this;
        }

        IInteractionFinisher IBuilderBase.WithMenu(Action<IBuilderMenu> menuBuilderConfig)
        {
            menuBuilderConfig(this);
            return this;
        }

        IBuilderBase IBuilderBase.PromptForInput(string inputKey, string prompt)
        {
            InputPrompts.Add(inputKey, prompt);
            return this;
        }

        IInteractionFinisher IBuilderBase.Prompt(string prompt,
            params (string answer, Func<IBuilderBase, IInteraction> config)[] options)
        {
            PromptInteractions = options.ToDictionary(
                o => o.answer,
                o => o.config(CreateChild()),
                StringComparer.Ordinal
            );

        return this;
    }

        IInteractionFinisher IBuilderBase.PromptCaseInsensitive(string prompt,
            params (string answer, Func<IBuilderBase, IInteraction> config)[] options)
        {
            Prompt = prompt;
            PromptInteractions = options.ToDictionary(
                o => o.answer,
                o => o.config(CreateChild()),
                StringComparer.OrdinalIgnoreCase
            );

            return this;
        }

        [PublicAPI]
        public interface IBuilderMenu
        {
            IBuilderMenu Option(string text, Func<IBuilderBase, IInteraction> optionConfig);
        }

        IBuilderMenu IBuilderMenu.Option(string text, Func<IBuilderBase, IInteraction> optionConfig)
        {
            MenuInteractions.Add(new KeyValuePair<string, IInteraction>(text, optionConfig(CreateChild())));
            return this;
        }

        [PublicAPI]
        public interface IInteractionFinisher
        {
            IInteractionContinuation RunAction(Action<TextReader, TextWriter, Dictionary<string, string>> action);
            IInteraction Build();
        }

        IInteractionContinuation IInteractionFinisher.RunAction(
            Action<TextReader, TextWriter, Dictionary<string, string>> action)
        {
            Action = action;
            return this;
        }

        IInteraction IInteractionFinisher.Build() => this;

        [PublicAPI]
        public interface IInteractionContinuation
        {
            IInteraction AndExit();
            IInteraction AndGoBack();
        }

        IInteraction IInteractionContinuation.AndExit()
        {
            ExitAfterAction = true;
            return this;
        }

        IInteraction IInteractionContinuation.AndGoBack()
        {
            ExitAfterAction = false;
            return this;
        }

        [PublicAPI]
        public interface IInteraction
        {
            bool Run();
        }

        bool IInteraction.Run()
        {
            if (ParentInputs != null)
                foreach (var kvp in ParentInputs)
                {
                    if (!Inputs.ContainsKey(kvp.Key))
                        Inputs.Add(kvp.Key, kvp.Value);
                }

            Out.WriteLine(Intro);

            GetInputs();

            bool repeat;
            do
            {
                repeat = RunInternal();
            } while (repeat);

            return !ExitAfterAction;
        }

        private bool RunInternal()
        {
            if (MenuInteractions.Any())
            {
                for (var i = 0; i < MenuInteractions.Count; ++i)
                    Out.WriteLine($"{i}. {MenuInteractions[i].Key}");

                var selection = ReadInt(In, Out, 0, MenuInteractions.Count - 1);
                return MenuInteractions[selection].Value.Run();
            }

            if (PromptInteractions?.Any() == true)
            {
                if(!string.IsNullOrWhiteSpace(Prompt))
                    Out.WriteLine(Prompt);

                var interaction = GetPromptInteraction(In, Out, PromptInteractions);
                return interaction.Run();
            }

            if (Action != null)
            {
                try
                {
                    Action(In, Out, Inputs);
                }
                catch (Exception exception)
                {
                    Out.WriteLine($"{exception.GetType().Name} has been thrown during action execution. Details below.");
                    WriteException(Out, exception);
                }
                return false;
            }

            Out.WriteLine("The interaction has not been configured properly: no Menu, Prompts or Action have been found. This is a fatal error.");
            throw new ApplicationException("Unexpected configuration error.");
        }

        private void GetInputs()
        {
            foreach (var inputPrompt in InputPrompts)
            {
                var current = Inputs.ContainsKey(inputPrompt.Key) ? Inputs[inputPrompt.Key] : string.Empty;
                Out.Write($"{inputPrompt.Value} [{current}]: ");

                var str = In.ReadLine();
                if (!string.IsNullOrEmpty(str)) // current if user just pressed enter
                    Inputs[inputPrompt.Key] = str;
            }
        }

        #region Helpers

        private static int ReadInt(TextReader input, TextWriter output, int min, int max)
        {
            string str;
            do
            {
                output.Write($"[{min} - {max}]: ");
                str = input.ReadLine();
            } while (str == null || !int.TryParse(str, out var parsed) || parsed < min || parsed > max);

            return int.Parse(str);
        }

        private static IInteraction GetPromptInteraction(TextReader input, TextWriter output, Dictionary<string, IInteraction> dict)
        {
            string str;
            do
            {
                output.Write($"[{string.Join(", ", dict.Keys)}]: ");
                str = input.ReadLine();
            } while (str == null || !dict.ContainsKey(str));

            return dict[str];
        }

        private static void WriteException(TextWriter output, Exception exception)
        {
            while (true)
            {
                if (exception == null) return;
                output.WriteLine(exception.Message);
                output.WriteLine(exception.StackTrace);

                if (exception.InnerException != null)
                {
                    exception = exception.InnerException;
                    continue;
                }

                break;
            }
        }

        #endregion
    }
}