using System.IO;

public class SubStream : Stream
{
    //SubStream
    private Stream baseStream;
    private long baseStreamOrigin;
    private long baseStreamSize;
    private long baseStreamReportedSize;

    private long currentPosition;
    private long lastKnownBaseStreamPosition;

    //Stream
    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => true;

    public override long Length => baseStreamSize;

    public override long Position
    {
        get => currentPosition;
        set
        {
            this.Seek(value, SeekOrigin.Begin);
        }
    }

    private bool IsSeekRequired()
    {
        long posDifference = baseStream.Position - baseStreamOrigin;
        return posDifference != currentPosition;
    }

    private void SeekIfNeeded()
    {
        if (IsSeekRequired())
        {
            baseStream.Seek(currentPosition + baseStreamOrigin, SeekOrigin.Begin);
        }
        lastKnownBaseStreamPosition = baseStream.Position;
    }

    public override void Flush()
    {
        baseStream.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        SeekIfNeeded();

        int maxRead = (int)(baseStreamSize - currentPosition);
        if (count > maxRead)
            count = maxRead;

        long actualRead = baseStream.Read(buffer, offset, count);
        currentPosition += actualRead;
        return (int)actualRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long totalSubstreamOffset = 0;
        switch (origin)
        {
            case SeekOrigin.Begin:
                totalSubstreamOffset = offset;
                break;
            case SeekOrigin.Current:
                totalSubstreamOffset = currentPosition + offset;
                break;
            case SeekOrigin.End:
                totalSubstreamOffset = baseStreamSize + offset;
                break;

        }
        
        if (totalSubstreamOffset > baseStreamSize)
            totalSubstreamOffset = baseStreamSize;

        currentPosition = totalSubstreamOffset;
        totalSubstreamOffset += baseStreamOrigin;

        baseStream.Seek(totalSubstreamOffset, SeekOrigin.Begin);
        return currentPosition;
    }

    public override void SetLength(long value)
    {
        throw new System.NotImplementedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        SeekIfNeeded();

        int maxWrite = (int)(baseStreamSize - currentPosition);
        if (count > maxWrite)
            count = maxWrite;

        baseStream.Write(buffer, offset, count);
        currentPosition += count;
    }

    //ctor
    public SubStream(Stream baseStream, long origin, long size)
    {
        if (!baseStream.CanSeek)
            throw new System.ArgumentException("SubStream baseStream must be seek-able", "baseStream");
        
        this.baseStream = baseStream;
        this.baseStreamOrigin = origin;
        this.baseStreamSize = size;
        this.lastKnownBaseStreamPosition = baseStream.Position;
    }

}
