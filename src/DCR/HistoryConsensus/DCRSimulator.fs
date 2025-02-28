﻿namespace HistoryConsensus

open Action
open FailureTypes
open Graph
open HistoryValidation

module DCRSimulator =
    type EventState = bool * bool * bool // Included * Pending * Executed
    type DCRState = Map<EventId, EventState>

    let buildCounterMap history : Map<ActionId, int> =
        Graph.fold 
            (fun counterMap action ->
                Set.fold 
                    (fun counterMap' edge ->
                        let newValue = (Map.find edge counterMap') + 1
                        Map.add edge newValue counterMap')
                    counterMap 
                    action.Edges)
            (Map.map (fun _ _ -> 0) history.Nodes) // This maps every actionId to 0
            history

    let updateStateForRelation state eventId relationType =
        let (included, pending, executed) = Map.find eventId state

        match relationType with
        | SetsPending -> Map.add eventId (included, true, executed) state
        | Includes    -> Map.add eventId (true, pending, executed)  state
        | Excludes    -> Map.add eventId (false, pending, executed) state
        | _ -> failwith "Wrong relation type in model!"

    let isExecutable conditions milestones state eventToExecute =
        match Map.tryFind eventToExecute state with
        | None -> false
        | Some (included,pending,executed) -> 
            if not included
            then false
            else
                let relevantConditions = Set.filter (fun (fromId, toId, actionType) -> fromId = eventToExecute) conditions
                if Set.fold 
                    (fun exec (_,conditionEventId,_) ->
                        let (included, _, executed) = Map.find conditionEventId state
                        exec && (not included || executed)) // If included and not executed -> Not executable.
                    true // Assume executable
                    relevantConditions

                then
                    let relevantMilestones = Set.filter (fun (fromId, toId, actionType) -> fromId = eventToExecute) milestones
                    Set.fold
                        (fun exec (_,milestoneEventId,_) ->
                            let (included, pending, _) = Map.find milestoneEventId state
                            exec && (not included || not pending)) // If included and pending -> Not executable
                        true // Assume executable
                        relevantMilestones
                else
                    false

    let execute (state : DCRState) (rules : DCRRules) eventId : Result<DCRState, DCRState> =
        match Map.tryFind eventId state with
            | None -> Failure state
            | Some (included, _, _) ->
                let eventRules = Set.filter (fun (id,_,_) -> id = eventId) rules
                let conditions = Set.filter (fun (_,_,relationType) -> relationType = ActionType.ChecksCondition) eventRules
                let milestones = Set.filter (fun (_,_,relationType) -> relationType = ActionType.ChecksMilestone) eventRules

                let executedState = 
                    Set.fold
                        (fun state' (_,toEventId,relationType) ->
                            updateStateForRelation state' toEventId relationType)
                        (Map.add eventId (included, false, true) state) // Set executed state of current event.
                        (Set.filter (fun (_,_,relationType) -> relationType <> ActionType.ChecksCondition && relationType <> ActionType.ChecksMilestone) eventRules) // Only look at rules that are not conditions and milestones.

                if not <| isExecutable conditions milestones state eventId
                then Failure executedState // Not executable, this execution is illegal
                else Success executedState

    let updateCounterMap counterMap history actionId : Map<ActionId, int> =
        let counterMap' = Map.remove actionId counterMap // Remove the action that was just executed.

        Set.fold // Decrement all the actions that are pointed at by the executed action.
            (fun map toActionId -> 
                let newValue = (Map.find toActionId map) - 1
                Map.add toActionId newValue map)
            counterMap'
            (Graph.getNode history actionId).Edges


    let simulate (history : Graph) (initialState : DCRState) (rules : DCRRules) : Result<Graph, Graph> =
        let initialCounterMap = buildCounterMap history
        
        let rec simulateExecution state counterMap : Result<Graph, ActionId> =
            if Map.isEmpty counterMap
            then Success history
            else
                // Take the first action that has a counter of 0
                let ((eventId, _) as actionId,_) = 
                    Map.filter (fun _ count -> count = 0) counterMap |> Map.toSeq |> Seq.head

                let counterMap' = updateCounterMap counterMap history actionId // Update counters after execution

                match execute state rules eventId with // Execute if possible
                | Failure state' -> 
                    Failure actionId // Fail fast!
                | Success state' -> 
                    simulateExecution state' counterMap' // Recursive step

        match simulateExecution initialState initialCounterMap with
        | Failure actionId ->
            let oldAction = getNode history actionId
            Failure (addNode { oldAction with FailureTypes = Set.add ExecutedWithoutProperState oldAction.FailureTypes } history)
        | _ -> Success history