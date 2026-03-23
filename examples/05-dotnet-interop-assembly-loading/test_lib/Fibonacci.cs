namespace CSharpFibonacci;

public static class Fibonacci
{
    public static int Factorial(int n)
    {
        if (n == 0)
        {
            return 1;
        }
        
        return n * Factorial(n - 1);
    }

    public static int FibonacciNumber(int n)
    {
        if (n <= 1) return n;
        return FibonacciNumber(n - 1) + FibonacciNumber(n - 2);
    }
}
