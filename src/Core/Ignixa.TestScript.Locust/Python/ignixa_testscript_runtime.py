import itertools


SUPPORTED_SCHEMA_MAJOR = 1
_USER_ORDINALS = itertools.count()


def initialize_user(document, user):
    major = int(document["schemaVersion"].split(".", 1)[0])
    if major != SUPPORTED_SCHEMA_MAJOR:
        raise RuntimeError(
            f"Unsupported TestScript IR schema {document['schemaVersion']}"
        )
    return {
        "iteration": 0,
        "ordinal": next(_USER_ORDINALS),
        "user": user,
    }


def execute(document, user, state):
    state["iteration"] += 1
    raise RuntimeError("Runtime execution is not implemented")
