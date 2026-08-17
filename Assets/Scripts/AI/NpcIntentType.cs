namespace CityStateSim.AI
{
    public enum NpcIntentType
    {
        ContinueCurrentAction = 0,
        TalkToPlayer = 1,
        TalkToNpc = 2,
        MoveToLocation = 3,
        WorkAtLocation = 4,
        RestAtLocation = 5,
        ReactToEvent = 6,
        AvoidActor = 8,
        JoinFestival = 9,
        SelfTalk = 10,
        AttendActivity = 11,
        FindActor = 12,
        FollowActor = 13
    }
}
