﻿namespace HistoryConsensus
open Action
open Newtonsoft.Json

module Graph =
    type Graph =
        {
            Nodes: Map<ActionId, Action>
        }

    val addNode : Action -> Graph -> Graph
    val removeNode : Action -> Graph -> Graph
    val addEdge : ActionId -> ActionId -> Graph -> Graph
    val removeEdge : ActionId -> ActionId -> Graph -> Graph
    val merge : Graph -> Graph -> Graph option
    val empty : Graph
    val getNode : Graph -> ActionId -> Action
    val transitiveReduction : Graph -> Graph
    val getBeginningNodes : Graph -> Action list
    val fold : ('a -> Action -> 'a) -> 'a -> Graph -> 'a
    val forall : (Action -> bool) -> Graph -> bool
    val exists : (Action -> bool) -> Graph -> bool