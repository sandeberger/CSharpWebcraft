namespace CSharpWebcraft.Utils;

/// <summary>
/// High-performance double-ended queue with circular buffer.
/// O(1) push and shift operations - replaces slow List shift which is O(n).
/// Port of utils.js Deque class.
/// </summary>
public class Deque<T>
{
    private T[] _buffer;
    private int _capacity;
    private int _head;
    private int _tail;
    public int Length { get; private set; }

    public Deque(int initialCapacity = 1024)
    {
        _capacity = initialCapacity;
        _buffer = new T[_capacity];
        _head = 0;
        _tail = 0;
        Length = 0;
    }

    public void Push(T item)
    {
        if (Length == _capacity)
            Grow();

        _buffer[_tail] = item;
        _tail = (_tail + 1) % _capacity;
        Length++;
    }

    public T? Shift()
    {
        if (Length == 0)
            return default;

        T item = _buffer[_head];
        _buffer[_head] = default!;
        _head = (_head + 1) % _capacity;
        Length--;
        return item;
    }

    public T? Peek()
    {
        if (Length == 0) return default;
        return _buffer[_head];
    }

    public void Clear()
    {
        _buffer = new T[_capacity];
        _head = 0;
        _tail = 0;
        Length = 0;
    }

    private void Grow()
    {
        int newCapacity = _capacity * 2;
        var newBuffer = new T[newCapacity];

        for (int i = 0; i < Length; i++)
            newBuffer[i] = _buffer[(_head + i) % _capacity];

        _buffer = newBuffer;
        _head = 0;
        _tail = Length;
        _capacity = newCapacity;
    }
}
