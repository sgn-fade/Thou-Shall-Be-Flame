# First-person hands animations

Animator states: Idle, Move, Shoot, Drag. Assign one Animation Clip to each state’s Motion field in the Animator window. Parameters are IsMoving, IsDragging, and Shoot. The presentation adapter listens to movement, drag, and shot events and writes those parameters. Gameplay components do not reference Animator.
