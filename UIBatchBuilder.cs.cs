using System;
using System.Collections.Generic;
using System.Windows.Forms;

public class UIBatchBuilder
{
    private readonly List<Action> left = new List<Action>();
    private readonly List<Action> right = new List<Action>();

    public void AddLeft(Action action)
    {
        left.Add(action);
    }

    public void AddRight(Action action)
    {
        right.Add(action);
    }

    public void Execute(Control container)
    {
        container.SuspendLayout();

        // PASS 1 : gauche
        foreach (var a in left)
            a();

        // PASS 2 : droite
        foreach (var a in right)
            a();

        container.ResumeLayout();
    }
}