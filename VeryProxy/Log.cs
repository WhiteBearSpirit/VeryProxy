using System;

namespace VeryProxy
{
    public static class Log
    {
        static string Timestamp
        {
            get { return ("[" + DateTime.Now.ToString("HH:mm:ss") + "] "); }
        }

        public static void Data(object o)
        {
            Line(o.GetType() + " Data:" + Environment.NewLine);
            Console.Write(o.ToString() + Environment.NewLine);
        }
        public static void Line()
        {
            Console.Write(Environment.NewLine + Timestamp); 
        }
        public static void Line(object o)
        {
            Console.Write(Environment.NewLine + Timestamp + 
                o.ToString().Replace('\r', ' ').Replace('\n', ' '));
        }
        public static void SameLine(object o)
        {
            Console.Write(o.ToString().Replace('\r', ' ').Replace('\n', ' '));
        }
    }
}
