
namespace Indexer2
{
    public class Base
    {
        public virtual int this[int index]
        {
            get { return 0; }
            set { }
        }
    }

    public class Derived : Base
    {
        public override int this[int index] // Ok
        {
            get { return 0;  }
            set { }
        }

        public int this[string index] // Ok
        {
            get { return 0; }
        }
    }

    public sealed class MoreDerived : Derived
    {
        public override int this[int index] // Ok
        {
            get { return 0; } // Non-virtual call to 
            set { }                         // Indexer.Derived::get_Item(index)
        }
    }


    public static class Program
    {
        public static void Main()
        {
            MoreDerived myRef = new MoreDerived();
            int hsh = myRef["blah"];
            myRef[0] = 42;
        }
    }
}
