using System;
using McDuck.InterAct;

namespace McDuck.Interact.Example
{
    class Program
    {
        static void Main()
        {
            InteractionBuilder.Create()
                .WithIntro("This is an intro text of the root level")
                .PromptForInput("root_input", "Provide some root level input. It will be passed down.")
                .WithMenu(menu => menu
                    .Option("First option that will print root input and exit app", opt
                        => opt
                            .RunAction((reader, writer, inputs) =>
                                writer.WriteLine($"The root input was: {inputs["root_input"]}"))
                            .AndExit()
                    )
                    .Option("Second option that will print and loop back here", opt
                        => opt
                            .RunAction((reader, writer, _) => writer.WriteLine("Printing action for second option"))
                            .AndGoBack()
                    )
                    .Option("Third option that goes into the prompt-driven interactions", opt
                        => opt
                            .PromptCaseInsensitive("Please select one of the actions",
                    ("alpha", intr
                                    => intr
                                        .RunAction((reader, writer, _) => writer.WriteLine("alpha"))
                                        .AndExit()
                                ),
                                ("beta", intr
                                    => intr
                                        .PromptForInput("root_input", "this allows to override root value for THIS level and below")
                                        .RunAction((reader, writer, inputs) => writer.WriteLine($"Current value of root value is {inputs["root_input"]}"))
                                        .AndExit()
                                )
                            )
                            .Build()
                    )
                )
                .Build()
                .Run();

            Console.WriteLine($"The {nameof(InteractionBuilder)} has finished. Review any output and press any key to exit.");
        }
    }
}
