public class Base { public int pblc; }
public class Derived : Base { private float prvt; }
public sealed class MoreDerived : Derived { }
public static class Program {
  public static void Main(string[] args) {
    MoreDerived m = new MoreDerived();
    m.prvt = 0.42F;
  }
}

