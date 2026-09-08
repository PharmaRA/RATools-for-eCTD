CREATE FUNCTION ratools_check_ctd_node() RETURNS trigger LANGUAGE plpgsql AS $body$
DECLARE
    definition ctd_definitions%ROWTYPE;
    parent_node ctd_node_instances%ROWTYPE;
    parent_definition ctd_definitions%ROWTYPE;
    attribute jsonb;
BEGIN
    IF TG_OP = 'UPDATE' AND NEW IS DISTINCT FROM OLD THEN
        RAISE EXCEPTION 'CTD application node identities are immutable' USING ERRCODE = '23514';
    END IF;
    SELECT * INTO STRICT definition FROM ctd_definitions
        WHERE "Version" = NEW."DefinitionVersion" AND "DefinitionKey" = NEW."DefinitionKey";
    IF NEW."ParentInstanceId" IS NULL THEN
        IF definition."ParentDefinitionKey" IS NOT NULL OR definition."Kind" = 'Extension' THEN
            RAISE EXCEPTION 'This CTD definition requires a parent' USING ERRCODE = '23514';
        END IF;
    ELSE
        SELECT * INTO parent_node FROM ctd_node_instances
            WHERE "Id" = NEW."ParentInstanceId" AND "ApplicationId" = NEW."ApplicationId";
        IF NOT FOUND THEN
            RAISE EXCEPTION 'The CTD parent must already exist in the same application' USING ERRCODE = '23503';
        END IF;
        SELECT * INTO STRICT parent_definition FROM ctd_definitions
            WHERE "Version" = parent_node."DefinitionVersion" AND "DefinitionKey" = parent_node."DefinitionKey";
        IF parent_node."DefinitionVersion" <> NEW."DefinitionVersion" OR
           (definition."Kind" = 'Extension' AND parent_definition."ExtensionPolicy" <> 'Allowed') OR
           (definition."Kind" <> 'Extension' AND definition."ParentDefinitionKey" IS DISTINCT FROM parent_node."DefinitionKey") THEN
            RAISE EXCEPTION 'The CTD parent definition is incompatible' USING ERRCODE = '23514';
        END IF;
    END IF;
    IF jsonb_typeof(NEW."IdentityAttributesJson") <> 'object' OR EXISTS (
        SELECT 1 FROM jsonb_each(NEW."IdentityAttributesJson") item
        WHERE jsonb_typeof(item.value) <> 'string' OR NOT EXISTS (
            SELECT 1 FROM jsonb_array_elements(definition."SchemaJson"->'attributes') allowed
            WHERE allowed->>'name' = item.key AND (allowed->>'identity')::boolean)) THEN
        RAISE EXCEPTION 'Invalid CTD identity attribute schema' USING ERRCODE = '23514';
    END IF;
    IF NEW."IdentityStatus" = 'Resolved' THEN
        FOR attribute IN SELECT * FROM jsonb_array_elements(definition."SchemaJson"->'attributes') LOOP
            IF (attribute->>'required')::boolean AND (attribute->>'identity')::boolean AND
               nullif(btrim(NEW."IdentityAttributesJson"->>(attribute->>'name')), '') IS NULL THEN
                RAISE EXCEPTION 'A resolved node requires its identity attributes' USING ERRCODE = '23514';
            END IF;
        END LOOP;
    END IF;
    RETURN NEW;
END;
$body$;

CREATE TRIGGER ratools_ctd_node_guard BEFORE INSERT OR UPDATE ON ctd_node_instances
    FOR EACH ROW EXECUTE FUNCTION ratools_check_ctd_node();

CREATE FUNCTION ratools_check_sequence_node() RETURNS trigger LANGUAGE plpgsql AS $body$
DECLARE
    instance ctd_node_instances%ROWTYPE;
    definition ctd_definitions%ROWTYPE;
    ancestor_id uuid;
    expected_section text;
    attribute jsonb;
BEGIN
    SELECT * INTO STRICT instance FROM ctd_node_instances
        WHERE "Id" = NEW."NodeInstanceId" AND "ApplicationId" = NEW."ApplicationId";
    SELECT * INTO STRICT definition FROM ctd_definitions
        WHERE "Version" = instance."DefinitionVersion" AND "DefinitionKey" = instance."DefinitionKey";
    IF NEW."ParentNodeInstanceId" IS DISTINCT FROM instance."ParentInstanceId" THEN
        RAISE EXCEPTION 'Sequence node parent differs from its business identity' USING ERRCODE = '23514';
    END IF;
    expected_section := definition."SectionPath";
    ancestor_id := instance."ParentInstanceId";
    WHILE expected_section IS NULL AND ancestor_id IS NOT NULL LOOP
        SELECT node."ParentInstanceId", parent."SectionPath" INTO ancestor_id, expected_section
            FROM ctd_node_instances node JOIN ctd_definitions parent
            ON parent."Version" = node."DefinitionVersion" AND parent."DefinitionKey" = node."DefinitionKey"
            WHERE node."Id" = ancestor_id AND node."ApplicationId" = NEW."ApplicationId";
    END LOOP;
    IF NEW."CtdSection" IS DISTINCT FROM expected_section THEN
        RAISE EXCEPTION 'Sequence node section differs from its definition' USING ERRCODE = '23514';
    END IF;
    IF jsonb_typeof(NEW."AttributesJson") <> 'object' OR EXISTS (
        SELECT 1 FROM jsonb_each(NEW."AttributesJson") item
        WHERE jsonb_typeof(item.value) <> 'string' OR NOT EXISTS (
            SELECT 1 FROM jsonb_array_elements(definition."SchemaJson"->'attributes') allowed
            WHERE allowed->>'name' = item.key)) THEN
        RAISE EXCEPTION 'Unknown or invalid sequence node attributes' USING ERRCODE = '23514';
    END IF;
    FOR attribute IN SELECT * FROM jsonb_array_elements(definition."SchemaJson"->'attributes') LOOP
        IF (attribute->>'identity')::boolean AND
           NEW."AttributesJson"->(attribute->>'name') IS DISTINCT FROM instance."IdentityAttributesJson"->(attribute->>'name') THEN
            RAISE EXCEPTION 'Sequence node cannot change business identity attributes' USING ERRCODE = '23514';
        END IF;
        IF NEW."MetadataStatus" = 'Complete' AND (attribute->>'required')::boolean AND
           nullif(btrim(NEW."AttributesJson"->>(attribute->>'name')), '') IS NULL THEN
            RAISE EXCEPTION 'Complete sequence node has missing metadata' USING ERRCODE = '23514';
        END IF;
    END LOOP;
    IF NEW."MetadataStatus" = 'Complete' AND (instance."IdentityStatus" <> 'Resolved' OR
       (definition."Kind" = 'Extension' AND nullif(btrim(NEW."Title"), '') IS NULL) OR
       EXISTS (SELECT 1 FROM sequence_nodes parent WHERE parent."ApplicationId" = NEW."ApplicationId" AND
          parent."SequenceNumber" = NEW."SequenceNumber" AND parent."NodeInstanceId" = NEW."ParentNodeInstanceId" AND
          parent."MetadataStatus" <> 'Complete')) THEN
        RAISE EXCEPTION 'Unresolved context cannot be marked complete' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$body$;

CREATE TRIGGER ratools_sequence_node_guard BEFORE INSERT OR UPDATE ON sequence_nodes
    FOR EACH ROW EXECUTE FUNCTION ratools_check_sequence_node();

CREATE FUNCTION ratools_preserve_ctd_definition() RETURNS trigger LANGUAGE plpgsql AS $body$
BEGIN
    RAISE EXCEPTION 'Published CTD definitions are immutable; add a new version' USING ERRCODE = '23514';
END;
$body$;

CREATE TRIGGER ratools_ctd_definition_guard BEFORE UPDATE OR DELETE ON ctd_definitions
    FOR EACH ROW EXECUTE FUNCTION ratools_preserve_ctd_definition();
