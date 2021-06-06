namespace DataLinkNetwork3
{
    public enum ResponseStatus
    {
        Undefined, // Неизвестен или не получен
        RR, // Receiver Ready
        RNR, // Receiver Not Ready
        REJ, // Reject
        SREJ // Selective Reject
    }
}