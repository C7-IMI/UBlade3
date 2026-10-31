using System;

namespace UBlade3.PlanVM
{
    /// <summary>
    /// AgendaItem in src/planvm/agendaitem.{h,cpp}: the object-owned list item the plan machine
    /// adds and removes. The bounded slice ports the base list lifecycle (Activate 0x0040b990,
    /// Detach 0x0040bb10, OnListChanged 0x0040b930) and the timer item the selected flow needs;
    /// the steering/input/avoidance item classes stay unported and are reported as telemetry.
    /// </summary>
    public abstract class PlanAiAgendaItem
    {
        /// <summary>AgendaItemIDs value (planvm/agendaitemids.h), minus one before a bind.</summary>
        public short Kind = -1;

        /// <summary>AgendaItem::m_bReady; three kinds reuse it for their own state.</summary>
        public bool Ready = true;

        /// <summary>Set on a behaviour's slot items; a removal unlinks it, never frees it.</summary>
        public bool Persistent;

        public PlanAiObject Owner;

        public PlanAiAgendaItem Next;

        /// <summary>AgendaItem::Process (slot one).</summary>
        public virtual void Process(int nArg)
        {
        }

        /// <summary>AgendaItem::Release (slot two).</summary>
        public virtual void Release(int nArg)
        {
        }

        /// <summary>AgendaItem::Bind (slot four).</summary>
        public virtual void Bind(PlanAiHost host, PlanBehavior behavior)
        {
            Next = null;
            Ready = false;
            Owner = host.Actor;
        }

        /// <summary>AgendaItem::BindWithNode (slot three).</summary>
        public virtual void BindWithNode(PlanAiHost host, PlanBehavior behavior, PlanNode node, int nArg)
        {
            Bind(host, behavior);
        }

        /// <summary>AgendaItem::Activate (0x0040b990): insert in ascending kind order.</summary>
        public virtual bool Activate()
        {
            if (Owner == null)
            {
                OnListChanged();

                return true;
            }

            PlanAiAgendaItem previous = null;
            PlanAiAgendaItem node = Owner.Observers;

            while (node != null && Kind > node.Kind)
            {
                previous = node;

                if (node.Owner != Owner)
                {
                    node.Next = null;
                    node.Detach();
                }

                node = node.Next;
            }

            if (node == null)
            {
                if (previous != null)
                {
                    previous.Next = this;
                    Next = null;

                    return true;
                }

                Owner.Observers = this;
                Next = null;

                return true;
            }

            if (Kind == node.Kind && !IsReplaceable())
            {
                if (node.Persistent)
                {
                    Unlink(node);
                }
                else
                {
                    node.Detach();
                }
            }

            if (previous == null)
            {
                Next = Owner.Observers;
                Owner.Observers = this;

                return true;
            }

            Next = previous.Next;
            previous.Next = this;

            return true;
        }

        /// <summary>AgendaItem::Detach (0x0040bb10).</summary>
        public virtual void Detach()
        {
            if (Owner == null)
            {
                return;
            }

            if (this == Owner.Observers)
            {
                Owner.Observers = Next;
            }
            else
            {
                PlanAiAgendaItem node;

                for (node = Owner.Observers; node != null; node = node.Next)
                {
                    if (this == node.Next)
                    {
                        break;
                    }
                }

                if (node != null)
                {
                    node.Next = Next;
                    Next = null;
                }
                else
                {
                    OnListChanged();
                }
            }

            OnListChanged();
        }

        /// <summary>AgendaItem::GetReadyFlag (0x0040b920); all but three kinds answer zero.</summary>
        public virtual bool GetReadyFlag()
        {
            return false;
        }

        /// <summary>AgendaItem::IsReplaceable (slot eight, folded onto slot seven).</summary>
        public virtual bool IsReplaceable()
        {
            return false;
        }

        /// <summary>AgendaItem::OnListChanged (0x0040b930), without the pool return.</summary>
        public virtual void OnListChanged()
        {
            if (!Ready)
            {
                Owner = null;
                Next = null;
                Ready = true;
            }
        }

        /// <summary>Unlink without notifying (the persistent-item case in Activate).</summary>
        protected void Unlink(PlanAiAgendaItem node)
        {
            if (Owner == null || node == null)
            {
                return;
            }

            if (Owner.Observers == node)
            {
                Owner.Observers = node.Next;
                node.Next = null;

                return;
            }

            PlanAiAgendaItem at = Owner.Observers;

            while (at != null)
            {
                if (at.Next == node)
                {
                    at.Next = node.Next;
                    node.Next = null;

                    return;
                }

                at = at.Next;
            }
        }
    }

    /// <summary>
    /// CTimerActivity, src/planvm/ctimeractivity.{h,cpp}: countdown in the clock's milliseconds
    /// (the authored argument is in 10 ms steps, so the fire tick is now + argument * 10), then a
    /// posted scene message. Bind 0x00411a10, BindWithNode 0x00411a40, Process 0x00411b00,
    /// Release 0x00411c00. It always answers GetReadyFlag one, which is why entering a
    /// non-digressive behaviour detaches every pending timer (plan VM rule 2).
    /// </summary>
    public sealed class PlanAiTimerActivity : PlanAiAgendaItem
    {
        public const short AgendaTimerActivity = 18;

        public int FireTickMs;

        public int Event;

        public int TargetSlot;

        public int EventParam;

        public PlanAiWorld World;

        /// <summary>CTimerActivity::Bind (0x00411a10).</summary>
        public override void Bind(PlanAiHost host, PlanBehavior behavior)
        {
            Next = null;
            Kind = AgendaTimerActivity;
            Ready = false;
            Owner = host.Actor;
            World = host.World;
            FireTickMs = 0;
            Event = PlanAiConstants.MessageNoMessage;
            TargetSlot = PlanAiConstants.ModelIdNone;
            EventParam = 0;
        }

        /// <summary>CTimerActivity::BindWithNode (0x00411a40).</summary>
        public override void BindWithNode(PlanAiHost host, PlanBehavior behavior, PlanNode node, int nArg)
        {
            Next = null;
            Owner = host.Actor;
            Kind = AgendaTimerActivity;
            Ready = false;
            World = host.World;

            FireTickMs = host.World.ClockMs + node.Args[0].IntValue * 10;
            Event = node.Args[1].IntValue;
            TargetSlot = node.Args[2].IntValue;

            if (node.ArgCount == 4)
            {
                if (node.Args[3].Word.Value == PlanWord.TokenMessageData)
                {
                    EventParam = nArg;
                }
                else
                {
                    EventParam = node.Args[3].IntValue;
                }
            }
            else
            {
                // Plan VM rule 6: no fourth leaf means data 0, which is the documented CARRYROK
                // "GoForGoal"/"PassingPlay" re-arm defect the probe trace shows.
                EventParam = 0;
            }

            host.World.Log("timer-arm event=" + PlanAiWorld.DescribeMessage(Event) + " target=" + TargetSlot +
                           " data=" + EventParam + " fire=" + FireTickMs);
        }

        /// <summary>CTimerActivity::OnListChanged (0x00411ac0).</summary>
        public override void OnListChanged()
        {
            if (!Ready)
            {
                Owner = null;
                FireTickMs = 0;
                EventParam = 0;
                Next = null;
                Event = PlanAiConstants.MessageNoMessage;
                TargetSlot = PlanAiConstants.ModelIdNone;
                Ready = true;
            }
        }

        /// <summary>CTimerActivity::Process (0x00411b00).</summary>
        public override void Process(int nArg)
        {
            if (Ready)
            {
                Detach();

                return;
            }

            if (World.ClockMs > FireTickMs)
            {
                World.Log("timer-fire event=" + PlanAiWorld.DescribeMessage(Event) + " target=" + TargetSlot +
                          " data=" + EventParam + " fire=" + FireTickMs + " now=" + World.ClockMs);

                if (TargetSlot == PlanWord.TokenBroadcast)
                {
                    World.BroadcastEvent(Event, EventParam, Owner.Slot);
                }
                else
                {
                    World.PostSceneEvent(TargetSlot, Event, EventParam);
                }

                if (this == Next)
                {
                    World.Log("timer.chasing-tail terminated");

                    Detach();

                    return;
                }

                if (Next != null && !Next.Ready)
                {
                    Next.Process(nArg);
                }

                Detach();
            }

            if (this == Next)
            {
                World.Log("timer.chasing-tail terminated");

                Detach();

                return;
            }

            if (Next != null && !Next.Ready)
            {
                Next.Process(nArg);
            }
        }

        /// <summary>CTimerActivity::Release (0x00411c00).</summary>
        public override void Release(int nArg)
        {
            if (World.ClockMs > FireTickMs)
            {
                World.Log("timer-release event=" + PlanAiWorld.DescribeMessage(Event) + " target=" + TargetSlot +
                          " data=" + EventParam + " fire=" + FireTickMs + " now=" + World.ClockMs);

                if (TargetSlot == PlanWord.TokenBroadcast)
                {
                    World.BroadcastEvent(Event, EventParam, Owner.Slot);
                }
                else
                {
                    World.PostSceneEvent(TargetSlot, Event, EventParam);
                }

                if (Next != null)
                {
                    Next.Process(nArg);
                }

                Detach();
            }

            if (Next != null && !Next.Ready)
            {
                Next.Release(nArg);
            }
        }

        public override bool GetReadyFlag()
        {
            return true;
        }

        public override bool IsReplaceable()
        {
            return true;
        }
    }
}
