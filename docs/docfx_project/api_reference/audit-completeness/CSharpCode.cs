namespace Trellis.Docs.Audit;

internal static class CSharpCode
{
    /// <summary>
    /// Blanks string/char literals and comments without changing offsets or line numbers.
    /// Interpolation holes are blanked with their string, matching TRLDOC014's existing scope.
    /// </summary>
    public static string BlankCommentsAndLiterals(string source)
    {
        var buffer = source.ToCharArray();
        int i = 0;

        while (i < buffer.Length)
        {
            char c = buffer[i];

            if (c == '/' && i + 1 < buffer.Length && buffer[i + 1] == '/')
            {
                while (i < buffer.Length && buffer[i] != '\n')
                    buffer[i++] = ' ';
            }
            else if (c == '/' && i + 1 < buffer.Length && buffer[i + 1] == '*')
            {
                while (i < buffer.Length && !(buffer[i] == '*' && i + 1 < buffer.Length && buffer[i + 1] == '/'))
                    BlankChar(buffer, ref i);

                for (int k = 0; k < 2 && i < buffer.Length; k++)
                    buffer[i++] = ' ';
            }
            else if (c == '"')
            {
                i = BlankString(buffer, i);
            }
            else if (c == '\'')
            {
                buffer[i++] = ' ';

                while (i < buffer.Length && buffer[i] != '\'' && buffer[i] != '\n')
                {
                    if (buffer[i] == '\\' && i + 1 < buffer.Length)
                        buffer[i++] = ' ';

                    BlankChar(buffer, ref i);
                }

                if (i < buffer.Length && buffer[i] == '\'')
                    buffer[i++] = ' ';
            }
            else
            {
                i++;
            }
        }

        return new string(buffer);
    }

    private static void BlankChar(char[] buffer, ref int i)
    {
        if (buffer[i] != '\n')
            buffer[i] = ' ';

        i++;
    }

    private static int BlankString(char[] buffer, int i)
    {
        // Raw strings close with a quote run at least as long as their opening run.
        int quotes = 0;
        while (i + quotes < buffer.Length && buffer[i + quotes] == '"')
            quotes++;

        if (quotes >= 3)
        {
            int fence = quotes;

            for (int k = 0; k < fence; k++)
                buffer[i++] = ' ';

            while (i < buffer.Length)
            {
                int run = 0;
                while (i + run < buffer.Length && buffer[i + run] == '"')
                    run++;

                if (run >= fence)
                {
                    for (int k = 0; k < run; k++)
                        buffer[i++] = ' ';

                    return i;
                }

                BlankChar(buffer, ref i);
            }

            return i;
        }

        // Both interpolated-verbatim prefix orderings are legal: $@"..." and @$"...".
        bool verbatim = (i > 0 && buffer[i - 1] == '@')
            || (i > 1 && buffer[i - 1] == '$' && buffer[i - 2] == '@');
        buffer[i++] = ' ';

        while (i < buffer.Length)
        {
            if (buffer[i] == '"')
            {
                if (verbatim && i + 1 < buffer.Length && buffer[i + 1] == '"')
                {
                    buffer[i++] = ' ';
                    buffer[i++] = ' ';
                    continue;
                }

                buffer[i++] = ' ';
                return i;
            }

            if (!verbatim && buffer[i] == '\\' && i + 1 < buffer.Length)
            {
                buffer[i++] = ' ';
                BlankChar(buffer, ref i);
                continue;
            }

            // A regular string fragment must not hide the rest of the fence.
            if (!verbatim && buffer[i] == '\n')
                return i;

            BlankChar(buffer, ref i);
        }

        return i;
    }
}