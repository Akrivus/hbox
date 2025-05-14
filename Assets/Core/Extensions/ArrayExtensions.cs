using System;
using System.Collections.Generic;
using System.Linq;

public static class ArrayExtensions
{
    public static ActorContext Get(this IEnumerable<ActorContext> contexts, Actor actor)
    {
        return contexts.FirstOrDefault(context => context.Reference == actor);
    }

    public static ActorController Get(this IEnumerable<ActorController> controllers, Actor actor)
    {
        return controllers.FirstOrDefault(controller => controller.Actor == actor);
    }

    public static ActorContext Get(this IEnumerable<ActorContext> actors, string name)
    {
        return actors.FirstOrDefault(actor => actor.Name == name);
    }

    public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> enumerable)
    {
        return enumerable.OrderBy(_ => Guid.NewGuid());
    }

    public static T Random<T>(this IEnumerable<T> enumerable)
    {
        return enumerable.ElementAt(UnityEngine.Random.Range(0, enumerable.Count()));
    }
}